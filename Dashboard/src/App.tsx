import { fetchColonySnapshot } from './api/colony';
import { fetchStatus, fetchSystemHealth } from './api/status';
import { findScope, ministerViews, scopeConfigs } from './dashboard/scopes';
import { formatLastRun } from './dashboard/selectors';
import { AnalyticsOverview } from './components/analytics/AnalyticsOverview';
import { DashboardHeader } from './components/layout/DashboardHeader';
import { ColonySidebar } from './components/layout/ColonySidebar';
import { InfoOverview } from './components/info/InfoOverview';
import { ScopeRail } from './components/layout/ScopeRail';
import { ViewTabs } from './components/layout/ViewTabs';
import { WorkspaceTitle } from './components/layout/WorkspaceTitle';
import { MinisterWorkspace } from './components/minister/MinisterWorkspace';
import { SystemOverview } from './components/system/SystemOverview';
import { useAdviceFeed } from './hooks/useAdviceFeed';
import { useDashboardSelection } from './hooks/useDashboardSelection';
import { useManualTriggers } from './hooks/useManualTriggers';
import { usePollingResource } from './hooks/usePollingResource';

const StatusPollMs = 3_000;
const SnapshotPollMs = 5_000;
const SystemHealthPollMs = 5_000;

export default function App() {
  const selection = useDashboardSelection();
  const triggers = useManualTriggers();
  const status = usePollingResource(fetchStatus, StatusPollMs);
  const snapshot = usePollingResource(fetchColonySnapshot, SnapshotPollMs);
  const systemHealth = usePollingResource(fetchSystemHealth, SystemHealthPollMs);
  const feed = useAdviceFeed();

  const activeScope = findScope(selection.selectedScope);
  const isSystem = activeScope.kind === 'system';
  const isInfo = activeScope.kind === 'info';
  const isAnalytics = activeScope.kind === 'analytics';

  return (
    <main className="dashboard-v2-shell">
      <DashboardHeader
        status={status.data}
        stream={feed.stream}
        triggerError={triggers.triggerState.error}
        triggerPending={triggers.triggerState.target === 'cabinet'}
        triggerDisabled={triggers.triggerState.target !== null}
        onTriggerCabinet={() => void triggers.triggerCabinetNow()}
      />

      <div className="dashboard-v2-grid">
        <ScopeRail
          activeScope={selection.selectedScope}
          scopes={scopeConfigs}
          onSelect={selection.selectScope}
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
          ) : isInfo ? (
            <InfoOverview />
          ) : isAnalytics ? (
            <AnalyticsOverview
              activeAdvice={feed.feed.activeAdvice}
              agenda={feed.agenda}
              events={feed.events}
              health={systemHealth.data}
              snapshot={snapshot.data}
              stream={feed.stream}
            />
          ) : (
            <>
              <WorkspaceTitle
                scope={activeScope}
                lastRunLabel={formatLastRun(activeScope, systemHealth.data)}
                triggerDisabled={triggers.triggerState.target !== null}
                triggerPending={triggers.triggerState.target === activeScope.key}
                onTrigger={() => void triggers.triggerMinisterNow(activeScope)}
              />
              <ViewTabs
                activeView={selection.selectedView}
                views={ministerViews}
                onSelect={selection.selectView}
              />
              <MinisterWorkspace
                activeAdvice={feed.feed.activeAdvice}
                agenda={feed.agenda}
                events={feed.events}
                previousAgenda={feed.previousAgenda}
                scope={activeScope}
                selectedView={selection.selectedView}
                systemHealth={systemHealth.data}
              />
            </>
          )}
        </section>

        <ColonySidebar
          snapshot={snapshot.data}
          error={snapshot.error}
          loadedAt={snapshot.loadedAt}
        />
      </div>
    </main>
  );
}
