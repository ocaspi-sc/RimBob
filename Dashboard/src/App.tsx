import { fetchColonySnapshot } from './api/colony';
import { fetchStatus, fetchSystemHealth } from './api/status';
import { findScope, isMinisterViewKey, ministerViews, scopeConfigs, viewForScope, viewsForScope } from './dashboard/scopes';
import { formatLastRun } from './dashboard/selectors';
import { AnalyticsOverview } from './components/analytics/AnalyticsOverview';
import { DevBlogOverview } from './components/devBlog/DevBlogOverview';
import { DashboardHeader } from './components/layout/DashboardHeader';
import { ColonySidebar } from './components/layout/ColonySidebar';
import { EndpointTimingFooter } from './components/layout/EndpointTimingFooter';
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
  const activeView = viewForScope(activeScope, selection.selectedView);
  const activePageKey = `${activeScope.key}:${activeView}`;
  const activeViews = viewsForScope(activeScope);
  const activeMinisterView = isMinisterViewKey(activeView) ? activeView : 'advice';
  const isSystem = activeScope.kind === 'system';
  const isInfo = activeScope.kind === 'info';
  const isAnalytics = activeScope.kind === 'analytics';
  const isDevBlog = activeScope.kind === 'dev_blog';
  const staleSnapshot = systemHealth.data &&
    !systemHealth.data.runtime.rimapi_reachable &&
    systemHealth.data.colony_snapshot.has_snapshot
    ? systemHealth.data.colony_snapshot
    : null;

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
              selectedView={activeView}
              views={activeViews}
              onSelectView={selection.selectView}
            />
          ) : isInfo ? (
            <InfoOverview
              selectedView={activeView}
              views={activeViews}
              onSelectView={selection.selectView}
            />
          ) : isAnalytics ? (
            <AnalyticsOverview
              activeAdvice={feed.feed.activeAdvice}
              agenda={feed.agenda}
              events={feed.events}
              health={systemHealth.data}
              snapshot={snapshot.data}
              stream={feed.stream}
              selectedView={activeView}
              views={activeViews}
              onSelectView={selection.selectView}
            />
          ) : isDevBlog ? (
            <DevBlogOverview
              selectedView={activeView}
              views={activeViews}
              onSelectView={selection.selectView}
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
                activeView={activeMinisterView}
                ariaLabel={`${activeScope.label} inspection views`}
                views={ministerViews}
                onSelect={selection.selectView}
              />
              <MinisterWorkspace
                activeAdvice={feed.feed.activeAdvice}
                agenda={feed.agenda}
                chains={feed.feed.chains}
                events={feed.events}
                previousAgenda={feed.previousAgenda}
                scope={activeScope}
                selectedView={activeMinisterView}
                stateSummaries={feed.feed.stateSummaries}
                systemHealth={systemHealth.data}
              />
            </>
          )}
          <EndpointTimingFooter pageKey={activePageKey} />
        </section>

        <ColonySidebar
          snapshot={snapshot.data}
          error={snapshot.error}
          loadedAt={snapshot.loadedAt}
          staleSnapshot={staleSnapshot}
        />
      </div>
    </main>
  );
}
