import { useEffect, useRef, useState } from 'react';
import { AgendaTab, PromptFull } from './components/AgendaTab';
import { Sidebar } from './components/Sidebar';
import { AlertsTab } from './components/AlertsTab';
import { BriefingTab } from './components/BriefingTab';
import { fetchLatestAgenda, triggerRefresh } from './api/agenda';
import { fetchColonySnapshot } from './api/colony';
import { fetchStatus } from './api/status';
import { subscribeAgendaUpdates } from './api/adviceStream';
import type { MayorAgenda } from './types/agenda';
import type { AdviceItem } from './types/advice';
import type { ColonySnapshot } from './types/colony';
import type { RimAIStatus } from './types/status';

type TabKey = 'agenda' | 'prompt' | 'alerts' | 'briefing' | 'log' | 'autonomy';

const tabs: Array<{ key: TabKey; label: string; ready: boolean; note?: string }> = [
  { key: 'agenda',   label: 'Agenda',   ready: true },
  { key: 'prompt',   label: 'Prompt',   ready: true },
  { key: 'alerts',   label: 'Alerts',   ready: true },
  { key: 'briefing', label: 'Briefing', ready: true },
  { key: 'log',      label: 'Log',      ready: false, note: 'Coming in M2' },
  { key: 'autonomy', label: 'Autonomy', ready: false, note: 'Coming in M2' },
];

const SnapshotPollMs = 5_000;
const StatusPollMs   = 3_000;

type ChipState = 'ok' | 'warn' | 'error' | 'active';

function StatusChip({ label, state, sub }: { label: string; state: ChipState; sub?: string }) {
  return (
    <span className={`status-chip ${state}`}>
      <span className="chip-dot" aria-hidden />
      {label}
      {sub && <span className="chip-sub">{sub}</span>}
    </span>
  );
}

export default function App() {
  const [agenda, setAgenda]     = useState<MayorAgenda | null>(null);
  const [previous, setPrevious] = useState<MayorAgenda | null>(null);
  const [advice, setAdvice]     = useState<AdviceItem[]>([]);
  const [snapshot, setSnapshot] = useState<ColonySnapshot | null>(null);
  const [active, setActive]     = useState<TabKey>('agenda');
  const [status, setStatus]     = useState<RimAIStatus | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const refreshAbort = useRef<AbortController | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchLatestAgenda()
      .then(latest => { if (!cancelled && latest) setAgenda(latest); })
      .catch(err => console.error('initial fetchLatestAgenda failed', err));

    const unsubscribe = subscribeAgendaUpdates(
      next => {
        setAgenda(curr => {
          if (curr && curr.version === next.version) return curr;
          setPrevious(curr);
          return next;
        });
      },
      item => {
        setAdvice(curr => {
          const filtered = curr.filter(existing => existing.id !== item.id);
          return [item, ...filtered].sort(compareAdvice);
        });
      },
    );

    return () => {
      cancelled = true;
      unsubscribe();
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    const ctrl = new AbortController();

    const load = async () => {
      try {
        const s = await fetchColonySnapshot(ctrl.signal);
        if (!cancelled) setSnapshot(s);
      } catch (err) {
        if (!cancelled && (err as Error).name !== 'AbortError') {
          console.error('fetchColonySnapshot failed', err);
        }
      }
    };

    void load();
    const timer = setInterval(() => void load(), SnapshotPollMs);

    return () => {
      cancelled = true;
      ctrl.abort();
      clearInterval(timer);
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    const ctrl = new AbortController();

    const load = async () => {
      try {
        const s = await fetchStatus(ctrl.signal);
        if (!cancelled) setStatus(s);
      } catch (err) {
        if (!cancelled && (err as Error).name !== 'AbortError') {
          console.error('fetchStatus failed', err);
        }
      }
    };

    void load();
    const timer = setInterval(() => void load(), StatusPollMs);

    return () => {
      cancelled = true;
      ctrl.abort();
      clearInterval(timer);
    };
  }, []);

  const handleRefresh = async () => {
    if (refreshing) return;
    refreshAbort.current?.abort();
    const ctrl = new AbortController();
    refreshAbort.current = ctrl;
    setRefreshing(true);
    try {
      await triggerRefresh(ctrl.signal);
    } catch (err) {
      if ((err as Error).name !== 'AbortError') {
        console.error('triggerRefresh failed', err);
      }
    } finally {
      setRefreshing(false);
    }
  };

  const mayorChipState: ChipState = !status
    ? 'warn'
    : status.mayor_running
    ? 'active'
    : status.mayor_last_error
    ? 'error'
    : 'ok';

  const mayorChipSub = status?.mayor_running ? 'Running' : status?.mayor_last_error ? 'Error' : 'Idle';

  return (
    <main className="app-shell">
      <header className="topbar panel-box">
        <div>
          <div className="kicker">RimWorld Advisory Cabinet</div>
          <h1>RimAI Command</h1>
        </div>
        <div className="topbar-status">
          <StatusChip label="SERVER" state="ok" />
          <StatusChip
            label="RIMAPI"
            state={status ? (status.rimapi_reachable ? 'ok' : 'warn') : 'warn'}
          />
          <StatusChip
            label="GEMINI"
            state={status ? (status.llm_configured ? 'ok' : 'error') : 'warn'}
          />
          <StatusChip label="MAYOR" state={mayorChipState} sub={mayorChipSub} />
          <span className="divider" />
          <button
            className={`refresh-btn ${refreshing ? 'refreshing' : ''}`}
            onClick={() => void handleRefresh()}
            disabled={refreshing}
            title="Re-run briefing and generate a new agenda"
          >
            {refreshing ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>
      </header>

      <div className="command-grid">
        <nav className="tab-rail panel-box" aria-label="Dashboard sections">
          {tabs.map(t => (
            <button
              key={t.key}
              className={`tab-button ${active === t.key ? 'active' : ''}`}
              disabled={!t.ready}
              onClick={() => t.ready && setActive(t.key)}
              title={t.note}
            >
              <span>{t.label}</span>
              {!t.ready && <small>{t.note}</small>}
            </button>
          ))}
        </nav>

        <section className="main-console panel-box">
          {active === 'agenda' && <AgendaTab agenda={agenda} previous={previous} status={status} />}
          {active === 'alerts' && <AlertsTab advice={advice} />}
          {active === 'briefing' && <BriefingTab />}
          {active === 'prompt' && (
            <div className="prompt-page">
              <PromptFull />
            </div>
          )}
          {active !== 'agenda' && active !== 'prompt' && active !== 'alerts' && active !== 'briefing' && (
            <div className="empty-console">
              <span className="empty-code">MODULE LOCKED</span>
              {tabs.find(t => t.key === active)?.note ?? 'Coming soon'}
            </div>
          )}
        </section>

        <Sidebar snapshot={snapshot} />
      </div>
    </main>
  );
}

function severityRank(severity: AdviceItem['severity']): number {
  if (severity === 'critical') return 3;
  if (severity === 'high') return 2;
  if (severity === 'medium') return 1;
  return 0;
}

function compareAdvice(a: AdviceItem, b: AdviceItem): number {
  const severityDelta = severityRank(b.severity) - severityRank(a.severity);
  if (severityDelta !== 0) return severityDelta;
  return b.priority_score - a.priority_score;
}
