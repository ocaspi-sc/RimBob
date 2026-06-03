import { fetchPrompt } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForField, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, summarizeValue, tryParseJson } from '../shared/JsonTree';
import { SemanticLabel } from '../shared/SemanticIcon';

export function MinisterPromptView({ scope }: { scope: ScopeConfig }) {
  const prompt = useAsyncResource(
    signal => scope.status === 'live'
      ? fetchPrompt(scope.key, signal)
      : Promise.resolve(null),
    [scope.key, scope.status],
  );

  if (scope.status !== 'live') {
    return <EmptyState code="PROMPT NOT WIRED">{scope.displayLabel} is a planned minister scope.</EmptyState>;
  }

  if (prompt.loading) {
    return <EmptyState code="PROMPT">Loading prompt inspector.</EmptyState>;
  }

  if (prompt.error || !prompt.data) {
    return (
      <EmptyState code="PROMPT UNAVAILABLE">
        {prompt.error ?? `${scope.displayLabel} prompt endpoint returned no payload.`}
      </EmptyState>
    );
  }

  const parsedUser = tryParseJson(prompt.data.user);

  return (
    <div className="minister-view prompt-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.displayLabel}</span>
        <h2><SemanticLabel icon={iconForView('prompt')}><span>System Prompt</span></SemanticLabel></h2>
        <p>Exact prompt material for the next LLM call where the backend exposes it.</p>
      </header>

      <div className="prompt-meta">
        <span>{prompt.data.system.length.toLocaleString()} system chars</span>
        <span>{prompt.data.user.length.toLocaleString()} user chars</span>
      </div>

      <DisclosureSection
        title={<SemanticLabel icon={iconForField('user_prompt')}><span>User message</span></SemanticLabel>}
        defaultOpen
        meta={summarizeValue(parsedUser ?? prompt.data.user)}
      >
        {parsedUser === undefined ? (
          <pre className="text-dump">{prompt.data.user}</pre>
        ) : (
          <JsonTree value={parsedUser} />
        )}
      </DisclosureSection>

      <DisclosureSection
        title={<SemanticLabel icon={iconForField('system_prompt')}><span>System prompt</span></SemanticLabel>}
        meta={`${prompt.data.system.length.toLocaleString()} chars`}
      >
        <pre className="text-dump">{prompt.data.system}</pre>
      </DisclosureSection>
    </div>
  );
}
