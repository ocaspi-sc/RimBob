import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForScope, iconForView } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from '../shared/SemanticIcon';

export function WorkspaceTitle({
  lastRunLabel,
  llmPending,
  onRunLlm,
  onRunRules,
  rulesPending,
  scope,
  triggerDisabled,
}: {
  lastRunLabel: string;
  llmPending: boolean;
  onRunLlm: () => void;
  onRunRules: () => void;
  rulesPending: boolean;
  scope: ScopeConfig;
  triggerDisabled: boolean;
}) {
  const canTrigger = scope.kind === 'minister' && scope.status === 'live';
  const canRunLlm = canTrigger && scope.canRunLlm === true;
  const canRunRules = canTrigger && scope.canRunRules === true;
  const llmTitle = canRunLlm
    ? `Run ${scope.label}'s LLM path`
    : canTrigger
      ? `${scope.label} has no LLM trigger wired yet`
      : `${scope.label} is not wired yet`;
  const rulesTitle = canRunRules
    ? `Run ${scope.label}'s deterministic rules path only`
    : canTrigger
      ? `${scope.label} has no rules-only trigger wired yet`
      : `${scope.label} is not wired yet`;
  return (
    <header className="workspace-title">
      <div>
        <SemanticIconCue className="scope-title-icon" icon={iconForScope(scope.key)} size="sm" />
        <span className="eyebrow">{scope.status === 'live' ? 'Live scope' : 'Planned scope'}</span>
        <h2>{scope.label}</h2>
      </div>
      <div className="workspace-actions">
        <span className="last-run-time">{lastRunLabel}</span>
        <div className="workspace-trigger-buttons">
          <button
            type="button"
            className="trigger-button"
            disabled={!canRunLlm || triggerDisabled}
            aria-busy={llmPending}
            onClick={onRunLlm}
            title={llmTitle}
          >
            <SemanticIconCue icon={iconForView('raw_llm')} size="xs" />
            <span>{llmPending ? 'Running LLM...' : 'Run LLM'}</span>
          </button>
          <button
            type="button"
            className="trigger-button secondary"
            disabled={!canRunRules || triggerDisabled}
            aria-busy={rulesPending}
            onClick={onRunRules}
            title={rulesTitle}
          >
            <SemanticIconCue icon={iconForView('rules')} size="xs" />
            <span>{rulesPending ? 'Running Rules...' : 'Run Rules'}</span>
          </button>
        </div>
      </div>
    </header>
  );
}
