import { fetchRawLlmOutput } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, tryParseJson } from '../shared/JsonTree';

export function MinisterRawLlmView({ scope }: { scope: ScopeConfig }) {
  const output = useAsyncResource(
    signal => scope.status === 'live'
      ? fetchRawLlmOutput(scope.key, signal)
      : Promise.resolve(null),
    [scope.key, scope.status],
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

  if (output.data.status === 'not_seen_yet') {
    return (
      <div className="minister-view raw-llm-view">
        <header className="view-heading">
          <span className="eyebrow">{scope.label}</span>
          <h2>Raw LLM Output</h2>
          <p>Unnormalized model responses before schema parsing, tolerant repair, or advice rendering.</p>
        </header>

        <EmptyState code="NO RAW OUTPUT YET">
          {scope.label} has not recorded an LLM response since this Host process started.
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
        <h2>Raw LLM Output</h2>
        <p>Unnormalized model responses before schema parsing, tolerant repair, or advice rendering.</p>
      </header>

      <div className="prompt-meta">
        <span>{output.data.provider}</span>
        <span>{output.data.model}</span>
        <span>{apiKeyLabel}</span>
        <span>{output.data.status.replace(/_/g, ' ')}</span>
        <span>{output.data.latencyMs.toLocaleString()} ms</span>
        <span>{new Date(output.data.capturedAt).toLocaleString()}</span>
      </div>

      <DisclosureSection title="Raw response" defaultOpen meta={`${output.data.text.length.toLocaleString()} chars`}>
        {parsed === undefined ? (
          <pre className="text-dump">{output.data.text}</pre>
        ) : (
          <JsonTree value={parsed} />
        )}
      </DisclosureSection>

      <DisclosureSection title="Capture metadata" meta={output.data.parseMode.replace(/_/g, ' ')}>
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
          }}
        />
      </DisclosureSection>
    </div>
  );
}

function formatApiKey(label: string | null, index: number | null): string {
  if (index === null) return 'api key n/a';
  const readable = label?.replace(/_/g, ' ') ?? (index === 1 ? 'primary' : `fallback ${index - 1}`);
  return `api key ${readable} (#${index})`;
}
