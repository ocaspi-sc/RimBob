import { fetchRawLlmOutput } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import type { MinisterTrace, SystemHealth } from '../../types/system';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, tryParseJson } from '../shared/JsonTree';

export function MinisterRawLlmView({
  scope,
  systemHealth,
}: {
  scope: ScopeConfig;
  systemHealth: SystemHealth | null;
}) {
  const latestTrace = findTrace(systemHealth, scope);
  const traceKey = latestTrace
    ? `${latestTrace.startedAt}|${latestTrace.completedAt ?? ''}|${latestTrace.status}`
    : 'no-trace';
  const output = useAsyncResource(
    signal => scope.status === 'live'
      ? fetchRawLlmOutput(scope.key, signal)
      : Promise.resolve(null),
    [scope.key, scope.status, traceKey],
  );

  if (scope.status !== 'live') {
    return <EmptyState code="RAW OUTPUT NOT WIRED">{scope.label} is a planned minister scope.</EmptyState>;
  }

  if (output.loading) {
    return <EmptyState code="RAW OUTPUT">Loading latest raw model output.</EmptyState>;
  }

  if (output.error || !output.data) {
    return (
      <EmptyState code="RAW OUTPUT UNAVAILABLE">
        {output.error ?? `${scope.label} raw LLM output endpoint returned no payload.`}
      </EmptyState>
    );
  }

  const stale = output.data.status !== 'not_seen_yet' && isOlderThanTrace(output.data.capturedAt, latestTrace);

  if (output.data.status === 'not_seen_yet') {
    const noOutputReason = latestTrace
      ? `${scope.label}'s latest run ${formatTraceTime(latestTrace)} did not record an LLM response. It may have stayed on the rules path or failed before provider text arrived.`
      : `${scope.label} has not recorded an LLM response since this Host process started.`;

    return (
      <div className="minister-view raw-llm-view">
        <header className="view-heading">
          <span className="eyebrow">{scope.label}</span>
          <h2>🧾 Raw LLM Output</h2>
          <p>Unnormalized model responses before schema parsing, tolerant repair, or advice rendering.</p>
        </header>

        <EmptyState code="NO RAW OUTPUT YET">
          {noOutputReason}
        </EmptyState>
      </div>
    );
  }

  const parsed = tryParseJson(output.data.text);
  const apiKeyLabel = formatApiKey(output.data.apiKeyLabel, output.data.apiKeyIndex);

  return (
    <div className="minister-view raw-llm-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2>🧾 Raw LLM Output</h2>
        <p>Unnormalized model responses before schema parsing, tolerant repair, or advice rendering.</p>
      </header>

      <div className="prompt-meta">
        <span>{output.data.provider}</span>
        <span>{output.data.model}</span>
        <span>{apiKeyLabel}</span>
        <span>{stale ? 'stale' : output.data.status.replace(/_/g, ' ')}</span>
        <span>{output.data.latencyMs.toLocaleString()} ms</span>
        <span>{new Date(output.data.capturedAt).toLocaleString()}</span>
      </div>

      {stale && latestTrace && (
        <EmptyState code="STALE RAW OUTPUT">
          {`Latest ${scope.label} run ${formatTraceTime(latestTrace)} did not record a newer LLM response. Showing the previous raw response captured ${new Date(output.data.capturedAt).toLocaleString()}.`}
        </EmptyState>
      )}

      <DisclosureSection title="📄 Raw response" defaultOpen meta={`${output.data.text.length.toLocaleString()} chars`}>
        {parsed === undefined ? (
          <pre className="text-dump">{output.data.text}</pre>
        ) : (
          <JsonTree value={parsed} />
        )}
      </DisclosureSection>

      <DisclosureSection title="🧷 Capture metadata" meta={output.data.parseMode.replace(/_/g, ' ')}>
        <JsonTree
          value={{
            minister: output.data.minister,
            provider: output.data.provider,
            model: output.data.model,
            api_key_index: output.data.apiKeyIndex,
            api_key_label: output.data.apiKeyLabel,
            captured_at: output.data.capturedAt,
            latency_ms: output.data.latencyMs,
            status: output.data.status,
            parse_mode: output.data.parseMode,
            system_prompt_chars: output.data.systemPromptChars,
            user_prompt_chars: output.data.userPromptChars,
            latest_run_started_at: latestTrace?.startedAt ?? null,
            latest_run_completed_at: latestTrace?.completedAt ?? null,
            latest_run_status: latestTrace?.status ?? null,
            stale_vs_latest_run: stale,
          }}
        />
      </DisclosureSection>
    </div>
  );
}

function findTrace(systemHealth: SystemHealth | null, scope: ScopeConfig): MinisterTrace | null {
  return systemHealth?.traces.find(trace =>
    trace.minister.toLowerCase() === scope.label.toLowerCase() ||
    trace.minister.toLowerCase() === scope.key.toLowerCase()
  ) ?? null;
}

function isOlderThanTrace(capturedAt: string, trace: MinisterTrace | null): boolean {
  if (!trace) return false;

  const capturedMs = Date.parse(capturedAt);
  const traceStartedMs = Date.parse(trace.startedAt);
  if (!Number.isFinite(capturedMs) || !Number.isFinite(traceStartedMs)) return false;

  return capturedMs < traceStartedMs;
}

function formatTraceTime(trace: MinisterTrace): string {
  const at = trace.completedAt ?? trace.startedAt;
  const label = trace.completedAt ? 'completed at' : 'started at';
  return `${label} ${new Date(at).toLocaleString()} (${trace.status})`;
}

function formatApiKey(label: string | null, index: number | null): string {
  if (index === null) return 'api key n/a';
  const readable = label?.replace(/_/g, ' ') ?? (index === 1 ? 'primary' : `fallback ${index - 1}`);
  return `api key ${readable} (#${index})`;
}
