import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForScope } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from '../shared/SemanticIcon';

export function WorkspaceTitle({
  lastRunLabel,
  onTrigger,
  scope,
  triggerDisabled,
  triggerPending,
}: {
  lastRunLabel: string;
  onTrigger: () => void;
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
      </div>
    </header>
  );
}
