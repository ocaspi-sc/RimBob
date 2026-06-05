import { fetchRawLlmOutput } from '../../api/ministers';
import type { ReactNode } from 'react';
import type { ScopeConfig } from '../../dashboard/scopes';
import { isScopeMinister } from '../../dashboard/selectors';
import { iconForField, iconForView } from '../../dashboard/semanticIcons';
import type { MinisterTrace, SystemHealth } from '../../types/system';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, tryParseJson } from '../shared/JsonTree';
import { SemanticLabel } from '../shared/SemanticIcon';
import { LlmInputPanel } from './LlmInputPanel';

export function MinisterRawLlmInputsSection({
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
    return null;
  }

  if (output.loading) {
    return (
      <RawLlmPanel>
        <EmptyState code="RAW OUTPUT">Loading latest raw model output.</EmptyState>
      </RawLlmPanel>
    );
  }

  if (output.error || !output.data) {
    return (
      <RawLlmPanel meta="unavailable">
        <EmptyState code="RAW OUTPUT UNAVAILABLE">
          {output.error ?? `${scope.displayLabel} raw LLM output endpoint returned no payload.`}
        </EmptyState>
      </RawLlmPanel>
    );
  }

  const stale = output.data.status !== 'not_seen_yet' && isOlderThanTrace(output.data.capturedAt, latestTrace);
  const runSummary = latestTrace ? summarizeTrace(latestTrace) : null;

  if (output.data.status === 'not_seen_yet') {
    const noOutputReason = latestTrace
      ? noRawOutputReason(scope.displayLabel, latestTrace)
      : `${scope.displayLabel} has not recorded an LLM response since this Host process started.`;

    return (
      <RawLlmPanel meta="no output yet">
        <EmptyState code="NO RAW OUTPUT YET">
          {noOutputReason}
        </EmptyState>

        {latestTrace && runSummary && (
          <DisclosureSection title="Latest run trace" defaultOpen meta={runSummary.meta}>
            <JsonTree value={traceMetadata(latestTrace, stale)} />
          </DisclosureSection>
        )}
      </RawLlmPanel>
    );
  }

  const parsed = tryParseJson(output.data.text);
  const apiKeyLabel = formatApiKey(output.data.apiKeyLabel, output.data.apiKeyIndex);

  return (
    <RawLlmPanel meta={stale ? 'stale' : output.data.status.replace(/_/g, ' ')}>
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
          {staleRawOutputReason(scope.displayLabel, latestTrace, output.data.capturedAt)}
        </EmptyState>
      )}

      <DisclosureSection
        title={<SemanticLabel icon={iconForField('raw_response')}><span>Raw response</span></SemanticLabel>}
        defaultOpen
        meta={`${output.data.text.length.toLocaleString()} chars`}
      >
        {parsed === undefined ? (
          <pre className="text-dump">{output.data.text}</pre>
        ) : (
          <JsonTree value={parsed} />
        )}
      </DisclosureSection>

      <DisclosureSection
        title={<SemanticLabel icon={iconForField('capture_metadata')}><span>Capture metadata</span></SemanticLabel>}
        meta={output.data.parseMode.replace(/_/g, ' ')}
      >
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
            latest_run: latestTrace ? traceMetadata(latestTrace, stale) : null,
          }}
        />
      </DisclosureSection>
    </RawLlmPanel>
  );
}

function RawLlmPanel({
  children,
  meta,
}: {
  children: ReactNode;
  meta?: string;
}) {
  return (
    <LlmInputPanel icon={iconForView('raw_llm')} title="Raw Output" meta={meta}>
      <header className="section-heading">
        <p className="notes-copy">Unnormalized model responses before schema parsing, tolerant repair, or advice rendering.</p>
      </header>
      {children}
    </LlmInputPanel>
  );
}

function findTrace(systemHealth: SystemHealth | null, scope: ScopeConfig): MinisterTrace | null {
  return systemHealth?.traces.find(trace => isScopeMinister(trace.minister, scope)) ?? null;
}

function isOlderThanTrace(capturedAt: string, trace: MinisterTrace | null): boolean {
  if (!trace) return false;

  const capturedMs = Date.parse(capturedAt);
  const traceStartedMs = Date.parse(trace.startedAt);
  if (!Number.isFinite(capturedMs) || !Number.isFinite(traceStartedMs)) return false;

  return capturedMs < traceStartedMs;
}

function noRawOutputReason(label: string, trace: MinisterTrace): string {
  const time = formatTraceTime(trace);
  if (trace.path === 'rules') {
    const rule = trace.ruleFired ? ` (${trace.ruleFired})` : '';
    return `${label}'s latest run ${time} used the rules path${rule}. No prior raw LLM response is available to show.`;
  }

  if (trace.path === 'llm_failed') {
    const reason = trace.escalationReason ? ` for ${trace.escalationReason}` : '';
    const error = trace.errorMessage ? `: ${trace.errorMessage}.` : '.';
    return `${label}'s latest run ${time} tried the LLM path${reason} but failed before provider text was captured${error} No prior raw LLM response is available to show.`;
  }

  if (trace.path === 'llm') {
    return `${label}'s latest run ${time} recorded an LLM path, but no raw response capture is available. Check logs and replay corpus for a capture gap.`;
  }

  return `${label}'s latest run ${time} did not record an LLM response. The backend trace path is ${trace.path}, and no prior raw LLM response is available to show.`;
}

function staleRawOutputReason(label: string, trace: MinisterTrace, capturedAt: string): string {
  const captured = new Date(capturedAt).toLocaleString();
  if (trace.path === 'rules') {
    const rule = trace.ruleFired ? ` (${trace.ruleFired})` : '';
    return `Latest ${label} run ${formatTraceTime(trace)} used the rules path${rule}. Showing the previous raw response captured ${captured}.`;
  }

  if (trace.path === 'llm_failed') {
    const reason = trace.escalationReason ? ` (${trace.escalationReason})` : '';
    const error = trace.errorMessage ? ` ${trace.errorMessage}.` : '';
    return `Latest ${label} run ${formatTraceTime(trace)} failed on the LLM path${reason}.${error} Showing the previous raw response captured ${captured}.`;
  }

  return `Latest ${label} run ${formatTraceTime(trace)} did not record a newer LLM response. Showing the previous raw response captured ${captured}.`;
}

function summarizeTrace(trace: MinisterTrace): { meta: string } {
  if (trace.path === 'rules' && trace.ruleFired) return { meta: `rules / ${trace.ruleFired}` };
  if (trace.path === 'llm_failed' && trace.errorType) return { meta: `llm failed / ${trace.errorType}` };
  if (trace.path === 'llm' && trace.escalationReason) return { meta: `llm / ${trace.escalationReason}` };
  return { meta: trace.path.replace(/_/g, ' ') };
}

function traceMetadata(trace: MinisterTrace, stale: boolean) {
  return {
    minister: trace.minister,
    trigger: trace.trigger,
    status: trace.status,
    path: trace.path,
    rule_fired: trace.ruleFired,
    escalation_reason: trace.escalationReason,
    error_type: trace.errorType,
    error_message: trace.errorMessage,
    advice_count: trace.adviceCount,
    flag_count: trace.flagCount,
    started_at: trace.startedAt,
    completed_at: trace.completedAt,
    stale_vs_latest_run: stale,
    note: trace.note,
  };
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
