import { fetchPrompt } from '../../api/client';
import type { ScopeConfig } from '../../dashboard/scopes';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, summarizeValue, tryParseJson } from '../shared/JsonTree';

export function MinisterPromptView({ scope }: { scope: ScopeConfig }) {
  const prompt = useAsyncResource(signal => fetchPrompt(scope.key, signal), [scope.key]);

  if (scope.status !== 'live') {
    return <EmptyState code="PROMPT NOT WIRED">{scope.label} is a planned minister scope.</EmptyState>;
  }

  if (prompt.loading) {
    return <EmptyState code="PROMPT">Loading prompt inspector.</EmptyState>;
  }

  if (prompt.error || !prompt.data) {
    return (
      <EmptyState code="PROMPT NOT EXPOSED">
        {prompt.error ?? `${scope.label} prompt endpoint is not exposed yet.`}
      </EmptyState>
    );
  }

  const parsedUser = tryParseJson(prompt.data.user);

  return (
    <div className="minister-view prompt-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2>System Prompt</h2>
        <p>Exact prompt material for the next LLM call where the backend exposes it.</p>
      </header>

      <div className="prompt-meta">
        <span>{prompt.data.system.length.toLocaleString()} system chars</span>
        <span>{prompt.data.user.length.toLocaleString()} user chars</span>
      </div>

      <DisclosureSection title="User message" defaultOpen meta={summarizeValue(parsedUser ?? prompt.data.user)}>
        {parsedUser === undefined ? (
          <pre className="text-dump">{prompt.data.user}</pre>
        ) : (
          <JsonTree value={parsedUser} />
        )}
      </DisclosureSection>

      <DisclosureSection title="System prompt" meta={`${prompt.data.system.length.toLocaleString()} chars`}>
        <pre className="text-dump">{prompt.data.system}</pre>
      </DisclosureSection>
    </div>
  );
}
