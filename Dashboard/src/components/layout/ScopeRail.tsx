import type { ScopeConfig, ScopeKey } from '../../dashboard/scopes';
import { iconForScope } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from '../shared/SemanticIcon';

export function ScopeRail({
  activeScope,
  scopes,
  onSelect,
}: {
  activeScope: ScopeKey;
  scopes: ScopeConfig[];
  onSelect: (scope: ScopeKey) => void;
}) {
  const consoleScopes = scopes.filter(scope => scope.kind !== 'minister');
  const ministerScopes = scopes.filter(scope => scope.kind === 'minister');

  return (
    <nav className="scope-rail panel-shell" aria-label="Cabinet scopes">
      <div className="rail-title">
        <span>RimBob</span>
        <small>Scopes</small>
      </div>
      <div className="scope-list">
        <ScopeGroup
          activeScope={activeScope}
          label="Console"
          onSelect={onSelect}
          scopes={consoleScopes}
        />
        <ScopeGroup
          activeScope={activeScope}
          label="Cabinet"
          onSelect={onSelect}
          scopes={ministerScopes}
        />
      </div>
    </nav>
  );
}

function ScopeGroup({
  activeScope,
  label,
  onSelect,
  scopes,
}: {
  activeScope: ScopeKey;
  label: string;
  onSelect: (scope: ScopeKey) => void;
  scopes: ScopeConfig[];
}) {
  return (
    <section className="scope-group" aria-label={label}>
      <span className="scope-group-label">{label}</span>
      {scopes.map(scope => (
        <button
          key={scope.key}
          type="button"
          className={`scope-button kind-${scope.kind} ${activeScope === scope.key ? 'active' : ''} ${scope.status}`}
          onClick={() => onSelect(scope.key)}
        >
          {scope.emoji ? (
            <span className="scope-emoji" aria-hidden="true">{scope.emoji}</span>
          ) : (
            <SemanticIconCue
              className="scope-icon"
              icon={iconForScope(scope.key)}
              size="xs"
            />
          )}
          <span className="scope-name">{scope.label}</span>
          <small>{scopeStatusLabel(scope)}</small>
        </button>
      ))}
    </section>
  );
}

function scopeStatusLabel(scope: ScopeConfig): string {
  if (scope.kind === 'home') return 'home';
  if (scope.kind === 'system') return 'ops';
  if (scope.kind === 'info') return 'ref';
  if (scope.kind === 'analytics') return 'data';
  if (scope.kind === 'dev_blog') return 'dev';
  return scope.status === 'live' ? 'live' : 'planned';
}
