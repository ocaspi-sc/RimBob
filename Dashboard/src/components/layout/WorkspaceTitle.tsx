import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForScope, iconForView } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from '../shared/SemanticIcon';

export function WorkspaceTitle({
  lastRunLabel,
  onTrigger,
  onTriggerRules,
  scope,
  triggerDisabled,
  triggerPending,
}: {
  lastRunLabel: string;
  onTrigger: () => void;
  onTriggerRules: () => void;
  scope: ScopeConfig;
  triggerDisabled: boolean;
  triggerPending: boolean;
}) {
  const canTrigger = scope.kind === 'minister' && scope.status === 'live';
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
            disabled={!canTrigger || triggerDisabled}
            aria-busy={triggerPending}
            onClick={onTrigger}
            title={canTrigger ? `Trigger ${scope.label} manually` : `${scope.label} is not wired yet`}
          >
            {triggerPending ? 'Running...' : canTrigger ? `Run ${scope.label} Now` : 'Not Wired'}
          </button>
          <button
            type="button"
            className="trigger-button secondary"
            disabled={!canTrigger || triggerDisabled}
            aria-busy={triggerPending}
            onClick={onTriggerRules}
            title={canTrigger ? `Run ${scope.label}'s rules-first evaluation` : `${scope.label} is not wired yet`}
          >
            <SemanticIconCue icon={iconForView('rules')} size="xs" />
            <span>{triggerPending ? 'Running...' : canTrigger ? 'Run Rules' : 'Rules Not Wired'}</span>
          </button>
        </div>
      </div>
    </header>
  );
}
