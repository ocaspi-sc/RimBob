import { fetchPrompt } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForField, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { MayorAgenda } from '../../types/agenda';
import type { SystemHealth } from '../../types/system';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, summarizeValue, tryParseJson } from '../shared/JsonTree';
import { SemanticLabel } from '../shared/SemanticIcon';
import { MinisterRagInputsSection } from './MinisterRagInputsSection';
import { MinisterRawLlmInputsSection } from './MinisterRawLlmInputsSection';

export function MinisterPromptView({
  agenda,
  scope,
  systemHealth,
}: {
  agenda: MayorAgenda | null;
  scope: ScopeConfig;
  systemHealth: SystemHealth | null;
}) {
  const prompt = useAsyncResource(
    signal => scope.status === 'live'
      ? fetchPrompt(scope.key, signal)
      : Promise.resolve(null),
    [scope.key, scope.status],
  );

  if (scope.status !== 'live') {
    return <EmptyState code="LLM NOT WIRED">{scope.displayLabel} is a planned minister scope.</EmptyState>;
  }

  const parsedUser = prompt.data ? tryParseJson(prompt.data.user) : undefined;

  return (
    <div className="minister-view prompt-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.displayLabel}</span>
        <h2><SemanticLabel icon={iconForView('prompt')}><span>LLM</span></SemanticLabel></h2>
        <p>Prompt material, retrieval context, and raw provider output where the backend exposes them.</p>
      </header>

      {prompt.loading && <EmptyState code="PROMPT">Loading prompt inspector.</EmptyState>}

      {!prompt.loading && (prompt.error || !prompt.data) && (
        <EmptyState code="PROMPT UNAVAILABLE">
          {prompt.error ?? `${scope.displayLabel} prompt endpoint returned no payload.`}
        </EmptyState>
      )}

      {prompt.data && (
        <>
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
        </>
      )}

      <MinisterRagInputsSection scope={scope} agenda={agenda} systemHealth={systemHealth} />
      <MinisterRawLlmInputsSection scope={scope} systemHealth={systemHealth} />
    </div>
  );
}
