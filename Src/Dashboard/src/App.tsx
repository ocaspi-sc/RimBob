import { useEffect, useState } from 'react';
import { AgendaTab } from './components/AgendaTab';
import { fetchLatestAgenda } from './api/agenda';
import { subscribeAgendaUpdates } from './api/adviceStream';
import type { MayorAgenda } from './types/agenda';

type TabKey = 'agenda' | 'alerts' | 'briefing' | 'log' | 'autonomy';

const tabs: Array<{ key: TabKey; label: string; ready: boolean; note?: string }> = [
  { key: 'agenda',    label: 'Agenda',    ready: true },
  { key: 'alerts',    label: 'Alerts',    ready: false, note: 'Coming in M5' },
  { key: 'briefing',  label: 'Briefing',  ready: false, note: 'Coming in M1+' },
  { key: 'log',       label: 'Log',       ready: false, note: 'Coming in M2' },
  { key: 'autonomy',  label: 'Autonomy',  ready: false, note: 'Coming in M2' },
];

export default function App() {
  const [agenda,   setAgenda]   = useState<MayorAgenda | null>(null);
  const [previous, setPrevious] = useState<MayorAgenda | null>(null);
  const [active,   setActive]   = useState<TabKey>('agenda');

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

  return (
    <main style={{ fontFamily: 'system-ui, sans-serif', maxWidth: 760, margin: '0 auto', padding: '2rem 1.5rem' }}>
      <header style={{ marginBottom: '1.5rem', borderBottom: '1px solid #e5e7eb', paddingBottom: '1rem' }}>
        <h1 style={{ margin: 0, fontSize: '1.5rem' }}>RimAI</h1>
        <p style={{ margin: '0.25rem 0 0', color: '#6b7280', fontSize: '0.9rem' }}>
          Colony advisor dashboard
        </p>
      </header>

      <nav style={{ display: 'flex', gap: '0.5rem', marginBottom: '1.5rem', flexWrap: 'wrap' }}>
        {tabs.map(t => (
          <button
            key={t.key}
            disabled={!t.ready}
            onClick={() => t.ready && setActive(t.key)}
            title={t.note}
            style={{
              padding: '0.4rem 0.9rem',
              border: 'none',
              borderBottom: active === t.key ? '2px solid #3730a3' : '2px solid transparent',
              background: 'transparent',
              fontSize: '0.9rem',
              fontWeight: active === t.key ? 600 : 400,
              color: t.ready ? (active === t.key ? '#1f2937' : '#6b7280') : '#d1d5db',
              cursor: t.ready ? 'pointer' : 'not-allowed',
            }}
          >
            {t.label}
          </button>
        ))}
      </nav>

      {active === 'agenda' && <AgendaTab agenda={agenda} previous={previous} />}
      {active !== 'agenda' && (
        <div style={{ color: '#9ca3af', textAlign: 'center', padding: '3rem 0' }}>
          {tabs.find(t => t.key === active)?.note ?? 'Coming soon'}
        </div>
      )}
    </main>
  );
}
