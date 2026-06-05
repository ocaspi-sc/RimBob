import { fetchColonySnapshot } from './api/colony';
import { fetchStatus, fetchSystemHealth } from './api/status';
import { findScope, isMinisterViewKey, scopeConfigs, viewForScope, viewsForScope } from './dashboard/scopes';
import { formatLastRun } from './dashboard/selectors';
import { AnalyticsOverview } from './components/analytics/AnalyticsOverview';
import { DevBlogOverview } from './components/devBlog/DevBlogOverview';
import { CabinetRunDialog } from './components/layout/CabinetRunDialog';
import { DashboardHeader } from './components/layout/DashboardHeader';
import { ColonySidebar } from './components/layout/ColonySidebar';
import { EndpointTimingFooter } from './components/layout/EndpointTimingFooter';
import { HomeOverview } from './components/home/HomeOverview';
import { InfoOverview } from './components/info/InfoOverview';
import { ScopeRail } from './components/layout/ScopeRail';
import { ViewTabs } from './components/layout/ViewTabs';
import { WorkspaceTitle } from './components/layout/WorkspaceTitle';
import { MinisterWorkspace } from './components/minister/MinisterWorkspace';
import { SystemOverview } from './components/system/SystemOverview';
import { useAdviceFeed } from './hooks/useAdviceFeed';
import { useDashboardSelection } from './hooks/useDashboardSelection';
import { useDashboardDocumentTitle } from './hooks/useDashboardDocumentTitle';
import { useDashboardReloadOnVersionChange } from './hooks/useDashboardReloadOnVersionChange';
import { useManualTriggers } from './hooks/useManualTriggers';
import { usePollingResource } from './hooks/usePollingResource';

const StatusPollMs = 5_000;
const SnapshotPollMs = 5_000;
const SystemHealthPollMs = 15_000;

export default function App() {
  const selection = useDashboardSelection();
  const status = usePollingResource(fetchStatus, StatusPollMs);
  const snapshot = usePollingResource(fetchColonySnapshot, SnapshotPollMs);
  const systemHealth = usePollingResource(fetchSystemHealth, SystemHealthPollMs);
  const feed = useAdviceFeed();
  const triggers = useManualTriggers(feed.cabinetRuns);
  useDashboardDocumentTitle(systemHealth.data, feed.runningVersion);
  useDashboardReloadOnVersionChange(systemHealth.data, feed.runningVersion);

  const activeScope = findScope(selection.selectedScope);
  const activeView = viewForScope(activeScope, selection.selectedView);
  const activePageKey = `${activeScope.key}:${activeView}`;
  const activeViews = viewsForScope(activeScope);
  const activeMinisterView = isMinisterViewKey(activeView) ? activeView : 'advice';
  const isHome = activeScope.kind === 'home';
  const isSystem = activeScope.kind === 'system';
  const isInfo = activeScope.kind === 'info';
  const isAnalytics = activeScope.kind === 'analytics';
  const isDevBlog = activeScope.kind === 'dev_blog';
  const hostApiLive = status.data !== null && status.error === null;
  const systemHealthFresh = systemHealth.data !== null && systemHealth.error === null;
  const staleSnapshot = systemHealth.data &&
    systemHealth.data.runtime.colony_state_origin !== 'live' &&
    systemHealth.data.colony_snapshot.has_snapshot
    ? systemHealth.data.colony_snapshot
    : null;

  return (
    <main className="dashboard-v2-shell">
      <DashboardHeader
        version={systemHealthFresh ? systemHealth.data?.version ?? null : null}
        hostProcessPath={systemHealthFresh ? systemHealth.data?.runtime.host_process_path ?? null : null}
        runtimeRoot={systemHealthFresh ? systemHealth.data?.runtime.runtime_root ?? null : null}
        status={status.data}
        statusError={status.error}
        statusLoadedAt={status.loadedAt}
        stream={feed.stream}
      />
      <CabinetRunDialog
        onClose={triggers.closeCabinetRunDialog}
        open={triggers.cabinetRunDialog.open}
        run={triggers.cabinetRunDialog.run}
      />

      <div className="dashboard-v2-grid">
        <ScopeRail
          activeScope={selection.selectedScope}
          scopes={scopeConfigs}
          onSelect={selection.selectScope}
        />

        <section className="main-workspace panel-shell" aria-label="Dashboard main workspace">
          {isHome ? (
            <HomeOverview
              advice={feed.feed.activeAdvice}
              agenda={feed.agenda}
              cabinetBusy={triggers.triggerState.target === 'cabinet' || triggers.triggerState.target === 'cabinet_rules'}
              cabinetPending={triggers.triggerState.target === 'cabinet'}
              cabinetRulesPending={triggers.triggerState.target === 'cabinet_rules'}
              flags={feed.feed.flags}
              health={systemHealth.data}
              hostApiLive={hostApiLive}
              onRunCabinet={() => void triggers.triggerCabinetNow()}
              onRunCabinetRules={() => void triggers.triggerCabinetRulesOnly()}
              onSelectMinisterView={selection.selectScopeView}
              recentCabinetRuns={feed.cabinetRuns}
              snapshot={snapshot.data}
              stateSummaries={feed.feed.stateSummaries}
              triggerError={triggers.triggerState.error}
            />
          ) : isSystem ? (
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
                triggerDisabled={triggers.triggerState.target !== null || !hostApiLive}
                llmPending={triggers.triggerState.target === `${activeScope.key}:llm`}
                rulesPending={triggers.triggerState.target === `${activeScope.key}:rules`}
                onRunLlm={() => void triggers.triggerMinisterLlm(activeScope)}
                onRunRules={() => void triggers.triggerMinisterRules(activeScope)}
              />
              <ViewTabs
                activeView={activeMinisterView}
                ariaLabel={`${activeScope.displayLabel} inspection views`}
                views={activeViews}
                onSelect={selection.selectView}
              />
              <MinisterWorkspace
                activeAdvice={feed.feed.activeAdvice}
                agenda={feed.agenda}
                chains={feed.feed.chains}
                events={feed.events}
                flags={feed.feed.flags}
                llmPending={triggers.triggerState.target === `${activeScope.key}:llm`}
                manualTriggerTarget={triggers.triggerState.target}
                onRunLlm={() => void triggers.triggerMinisterLlm(activeScope)}
                previousAgenda={feed.previousAgenda}
                scope={activeScope}
                selectedView={activeMinisterView}
                stateSummaries={feed.feed.stateSummaries}
                systemHealth={systemHealth.data}
                triggerDisabled={triggers.triggerState.target !== null || !hostApiLive}
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
