import type { DashboardViewDefinition, DashboardViewKey } from '../../dashboard/scopes';
import { iconForView } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from '../shared/SemanticIcon';

export function ViewTabs({
  ariaLabel = 'Workspace views',
  activeView,
  views,
  onSelect,
}: {
  activeView: DashboardViewKey;
  ariaLabel?: string;
  views: DashboardViewDefinition[];
  onSelect: (view: DashboardViewKey) => void;
}) {
  return (
    <div className="view-tabs" role="tablist" aria-label={ariaLabel}>
      {views.map(view => (
        <button
          key={view.key}
          type="button"
          role="tab"
          aria-selected={activeView === view.key}
          className={activeView === view.key ? 'active' : ''}
          onClick={() => onSelect(view.key)}
        >
          <SemanticIconCue className="view-tab-icon" icon={iconForView(view.key)} size="xs" />
          {view.label}
        </button>
      ))}
    </div>
  );
}
