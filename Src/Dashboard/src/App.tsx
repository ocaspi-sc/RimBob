import { useState } from 'react';
import { fetchColonySnapshot, fetchStatus, fetchSystemHealth } from './api/client';
import { ministerPanelRegistry, panelIdFor } from './dashboard/panelRegistry';
import { findScope, ministerViews, scopeConfigs, type MinisterViewKey, type ScopeConfig, type ScopeKey } from './dashboard/scopes';
import { DashboardHeader } from './components/layout/DashboardHeader';
import { ColonySidebar } from './components/layout/ColonySidebar';
import { ScopeRail } from './components/layout/ScopeRail';
import { ViewTabs } from './components/layout/ViewTabs';
import { MinisterAdviceView } from './components/minister/MinisterAdviceView';
import { MinisterBriefingView } from './components/minister/MinisterBriefingView';
import { MinisterPromptView } from './components/minister/MinisterPromptView';
import { MinisterRagView } from './components/minister/MinisterRagView';
import { MinisterRulesView } from './components/minister/MinisterRulesView';
import { SystemOverview } from './components/system/SystemOverview';
import { useAdviceFeed } from './hooks/useAdviceFeed';
import { usePollingResource } from './hooks/usePollingResource';
import type { AdviceItem } from './types/advice';
import type { MayorAgenda } from './types/agenda';
import type { DashboardEvent, SystemHealth } from './types/system';

const StatusPollMs = 3_000;
const SnapshotPollMs = 5_000;
const SystemHealthPollMs = 5_000;

export default function App() {
  const [selectedScope, setSelectedScope] = useState<ScopeKey>('system');
  const [selectedView, setSelectedView] = useState<MinisterViewKey>('advice');

  const status = usePollingResource(fetchStatus, StatusPollMs);
  const snapshot = usePollingResource(fetchColonySnapshot, SnapshotPollMs);
  const systemHealth = usePollingResource(fetchSystemHealth, SystemHealthPollMs);
  const feed = useAdviceFeed();

  const activeScope = findScope(selectedScope);
  const isSystem = activeScope.kind === 'system';

  const handleScopeSelect = (scope: ScopeKey) => {
    setSelectedScope(scope);
    if (findScope(scope).kind === 'minister') {
      setSelectedView('advice');
    }
  };

  return (
    <main className="dashboard-v2-shell">
      <DashboardHeader
        status={status.data}
        stream={feed.stream}
      />

      <div className="dashboard-v2-grid">
        <ScopeRail
          activeScope={selectedScope}
          scopes={scopeConfigs}
          onSelect={handleScopeSelect}
        />

        <section className="main-workspace panel-shell" aria-label="Dashboard main workspace">
          {isSystem ? (
            <SystemOverview
              status={status.data}
              health={systemHealth.data}
              healthError={systemHealth.error}
              stream={feed.stream}
              events={feed.events}
            />
          ) : (
            <>
              <WorkspaceTitle scope={activeScope} view={selectedView} />
              <ViewTabs
                activeView={selectedView}
                views={ministerViews}
                onSelect={setSelectedView}
              />
              <div className="panel-registry-note minister-registry">
                <span>{panelIdFor(activeScope.key, selectedView)}</span>
                {ministerPanelRegistry.map(panel => (
                  <code key={panel.id}>{panel.view}</code>
                ))}
              </div>
              <MinisterWorkspace
                activeAdvice={feed.feed.activeAdvice}
                agenda={feed.agenda}
                events={feed.events}
                previousAgenda={feed.previousAgenda}
                scope={activeScope}
                selectedView={selectedView}
                systemHealth={systemHealth.data}
              />
            </>
          )}
        </section>

        <ColonySidebar
          snapshot={snapshot.data}
          error={snapshot.error}
        />
      </div>
    </main>
  );
}

function WorkspaceTitle({
  scope,
  view,
}: {
  scope: ScopeConfig;
  view: MinisterViewKey;
}) {
  const viewLabel = ministerViews.find(item => item.key === view)?.label ?? view;
  return (
    <header className="workspace-title">
      <div>
        <span className="scope-emoji" aria-hidden>{scope.emoji}</span>
        <span className="eyebrow">{scope.status === 'live' ? 'Live scope' : 'Planned scope'}</span>
        <h2>{scope.label}</h2>
      </div>
      <strong>{viewLabel}</strong>
    </header>
  );
}

function MinisterWorkspace({
  activeAdvice,
  agenda,
  events,
  previousAgenda,
  scope,
  selectedView,
  systemHealth,
}: {
  activeAdvice: AdviceItem[];
  agenda: MayorAgenda | null;
  events: DashboardEvent[];
  previousAgenda: MayorAgenda | null;
  scope: ScopeConfig;
  selectedView: MinisterViewKey;
  systemHealth: SystemHealth | null;
}) {
  if (selectedView === 'prompt') {
    return <MinisterPromptView scope={scope} />;
  }

  if (selectedView === 'briefing') {
    return <MinisterBriefingView scope={scope} />;
  }

  if (selectedView === 'rag') {
    return <MinisterRagView scope={scope} agenda={agenda} systemHealth={systemHealth} />;
  }

  if (selectedView === 'rules') {
    return <MinisterRulesView scope={scope} events={events} advice={activeAdvice} />;
  }

  return (
    <MinisterAdviceView
      scope={scope}
      agenda={agenda}
      previousAgenda={previousAgenda}
      advice={activeAdvice}
    />
  );
}
