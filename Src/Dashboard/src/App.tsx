import { useEffect, useState } from 'react';
import { fetchColonySnapshot, fetchStatus, fetchSystemHealth, triggerCabinet, triggerMinister } from './api/client';
import { findScope, ministerViews, scopeConfigs, type MinisterViewKey, type ScopeConfig, type ScopeKey } from './dashboard/scopes';
import { DashboardHeader } from './components/layout/DashboardHeader';
import { ColonySidebar } from './components/layout/ColonySidebar';
import { ScopeRail } from './components/layout/ScopeRail';
import { ViewTabs } from './components/layout/ViewTabs';
import { MinisterAdviceView } from './components/minister/MinisterAdviceView';
import { MinisterBriefingView } from './components/minister/MinisterBriefingView';
import { MinisterPromptView } from './components/minister/MinisterPromptView';
import { MinisterRagView } from './components/minister/MinisterRagView';
import { MinisterRawLlmView } from './components/minister/MinisterRawLlmView';
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
const SelectedScopeStorageKey = 'rimai.dashboard.selectedScope';
const SelectedViewStorageKey = 'rimai.dashboard.selectedView';
type TriggerTarget = 'cabinet' | ScopeKey;

interface TriggerState {
  target: TriggerTarget | null;
  error: string | null;
}

export default function App() {
  const [selectedScope, setSelectedScope] = useState<ScopeKey>(() => readStoredScope());
  const [selectedView, setSelectedView] = useState<MinisterViewKey>(() => readStoredView());
  const [triggerState, setTriggerState] = useState<TriggerState>({ target: null, error: null });

  const status = usePollingResource(fetchStatus, StatusPollMs);
  const snapshot = usePollingResource(fetchColonySnapshot, SnapshotPollMs);
  const systemHealth = usePollingResource(fetchSystemHealth, SystemHealthPollMs);
  const feed = useAdviceFeed();

  const activeScope = findScope(selectedScope);
  const isSystem = activeScope.kind === 'system';

  useEffect(() => {
    writeStoredValue(SelectedScopeStorageKey, selectedScope);
  }, [selectedScope]);

  useEffect(() => {
    writeStoredValue(SelectedViewStorageKey, selectedView);
  }, [selectedView]);

  const handleScopeSelect = (scope: ScopeKey) => {
    setSelectedScope(scope);
    if (findScope(scope).kind === 'minister') {
      setSelectedView('advice');
    }
  };

  const runManualTrigger = async (
    target: TriggerTarget,
    label: string,
    action: () => Promise<unknown>,
  ) => {
    if (triggerState.target !== null) return;

    setTriggerState({ target, error: null });
    try {
      await action();
      setTriggerState({ target: null, error: null });
    } catch (error) {
      setTriggerState({
        target: null,
        error: `${label} failed: ${error instanceof Error ? error.message : String(error)}`,
      });
    }
  };

  const handleCabinetTrigger = () =>
    runManualTrigger('cabinet', 'Run Cabinet Now', () => triggerCabinet());

  const handleMinisterTrigger = (scope: ScopeConfig) =>
    runManualTrigger(scope.key, `Run ${scope.label} Now`, () => triggerMinister(scope.key));

  return (
    <main className="dashboard-v2-shell">
      <DashboardHeader
        status={status.data}
        stream={feed.stream}
        triggerError={triggerState.error}
        triggerPending={triggerState.target === 'cabinet'}
        triggerDisabled={triggerState.target !== null}
        onTriggerCabinet={() => void handleCabinetTrigger()}
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
              <WorkspaceTitle
                scope={activeScope}
                lastRunLabel={formatLastRun(activeScope, systemHealth.data)}
                triggerDisabled={triggerState.target !== null}
                triggerPending={triggerState.target === activeScope.key}
                onTrigger={() => void handleMinisterTrigger(activeScope)}
              />
              <ViewTabs
                activeView={selectedView}
                views={ministerViews}
                onSelect={setSelectedView}
              />
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

function readStoredScope(): ScopeKey {
  const stored = readStoredValue(SelectedScopeStorageKey);
  return isScopeKey(stored) ? stored : 'system';
}

function readStoredView(): MinisterViewKey {
  const stored = readStoredValue(SelectedViewStorageKey);
  return isMinisterViewKey(stored) ? stored : 'advice';
}

function readStoredValue(key: string): string | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStoredValue(key: string, value: string) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Storage can be unavailable in restricted browser contexts; dashboard state can still be session-only.
  }
}

function isScopeKey(value: string | null): value is ScopeKey {
  return typeof value === 'string' && scopeConfigs.some(scope => scope.key === value);
}

function isMinisterViewKey(value: string | null): value is MinisterViewKey {
  return typeof value === 'string' && ministerViews.some(view => view.key === value);
}

function WorkspaceTitle({
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
        <span className="scope-emoji" aria-hidden>{scope.emoji}</span>
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

function formatLastRun(scope: ScopeConfig, health: SystemHealth | null): string {
  if (scope.kind !== 'minister') return '';
  if (scope.status !== 'live') return 'Not wired';

  const trace = health?.traces.find(item =>
    item.minister.localeCompare(scope.label, undefined, { sensitivity: 'accent' }) === 0);

  if (!trace) return 'Last run never';
  const timestamp = trace.completedAt ?? trace.startedAt;
  const label = trace.status === 'running' ? 'Running since' : 'Last run';
  return `${label} ${formatTraceTime(timestamp)}`;
}

function formatTraceTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const now = new Date();
  const sameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();

  return sameDay
    ? date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
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

  if (selectedView === 'raw_llm') {
    return <MinisterRawLlmView scope={scope} />;
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
