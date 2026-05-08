import { useEffect, useState } from 'react';
import { AgendaTab } from './components/AgendaTab';
import { Sidebar } from './components/Sidebar';
import { fetchLatestAgenda } from './api/agenda';
import { fetchColonySnapshot } from './api/colony';
import { subscribeAgendaUpdates } from './api/adviceStream';
import type { MayorAgenda } from './types/agenda';
import type { ColonySnapshot } from './types/colony';

type TabKey = 'agenda' | 'alerts' | 'briefing' | 'log' | 'autonomy';

const tabs: Array<{ key: TabKey; label: string; ready: boolean; note?: string }> = [
  { key: 'agenda',   label: 'Agenda',   ready: true },
  { key: 'alerts',   label: 'Alerts',   ready: false, note: 'Coming in M5' },
  { key: 'briefing', label: 'Briefing', ready: false, note: 'Coming in M1+' },
  { key: 'log',      label: 'Log',      ready: false, note: 'Coming in M2' },
  { key: 'autonomy', label: 'Autonomy', ready: false, note: 'Coming in M2' },
];

const SnapshotPollMs = 5_000;

export default function App() {
  const [agenda, setAgenda] = useState<MayorAgenda | null>(null);
  const [previous, setPrevious] = useState<MayorAgenda | null>(null);
  const [snapshot, setSnapshot] = useState<ColonySnapshot | null>(null);
  const [active, setActive] = useState<TabKey>('agenda');

  useEffect(() => {
    let cancelled = false;
    fetchLatestAgenda()
      .then(latest => { if (!cancelled && latest) setAgenda(latest); })
      .catch(err => console.error('initial fetchLatestAgenda failed', err));

    const unsubscribe = subscribeAgendaUpdates(next => {
      setAgenda(curr => {
        if (curr && curr.version === next.version) return curr;
        setPrevious(curr);
        return next;
      });
    });

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

  const connectionState = agenda ? 'live' : 'standby';

  return (
    <main className="app-shell">
      <header className="topbar panel-box">
        <div>
          <div className="kicker">RimWorld Advisory Cabinet</div>
          <h1>RimAI Command</h1>
        </div>
        <div className="topbar-status">
          <span className={`status-light ${connectionState}`} aria-hidden />
          <span>{connectionState === 'live' ? 'Agenda feed live' : 'Awaiting first agenda'}</span>
          <span className="divider" />
          <span>Poll {SnapshotPollMs / 1000}s</span>
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
          {active === 'agenda' && <AgendaTab agenda={agenda} previous={previous} />}
          {active !== 'agenda' && (
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
