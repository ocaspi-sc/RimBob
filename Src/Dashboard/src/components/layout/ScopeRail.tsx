import type { ScopeConfig, ScopeKey } from '../../dashboard/scopes';

export function ScopeRail({
  activeScope,
  scopes,
  onSelect,
}: {
  activeScope: ScopeKey;
  scopes: ScopeConfig[];
  onSelect: (scope: ScopeKey) => void;
}) {
  return (
    <nav className="scope-rail panel-shell" aria-label="Cabinet scopes">
      <div className="rail-title">
        <span>Cabinet</span>
        <small>Scopes</small>
      </div>
      <div className="scope-list">
        {scopes.map(scope => (
          <button
            key={scope.key}
            type="button"
            className={`scope-button ${activeScope === scope.key ? 'active' : ''} ${scope.status}`}
            onClick={() => onSelect(scope.key)}
          >
            <span className="scope-emoji" aria-hidden>{scope.emoji}</span>
            <span className="scope-name">{scope.label}</span>
            <small>{scope.status === 'live' ? 'live' : 'planned'}</small>
          </button>
        ))}
      </div>
    </nav>
  );
}
