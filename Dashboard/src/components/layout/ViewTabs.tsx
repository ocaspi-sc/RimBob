import type { MinisterViewKey } from '../../dashboard/scopes';

export function ViewTabs({
  activeView,
  views,
  onSelect,
}: {
  activeView: MinisterViewKey;
  views: Array<{ key: MinisterViewKey; label: string }>;
  onSelect: (view: MinisterViewKey) => void;
}) {
  return (
    <div className="view-tabs" role="tablist" aria-label="Minister inspection views">
      {views.map(view => (
        <button
          key={view.key}
          type="button"
          role="tab"
          aria-selected={activeView === view.key}
          className={activeView === view.key ? 'active' : ''}
          onClick={() => onSelect(view.key)}
        >
          {view.label}
        </button>
      ))}
    </div>
  );
}
