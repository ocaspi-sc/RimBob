import type { ReactNode } from 'react';
import type { MayorAgenda, AgendaItem } from '../types/agenda';
import { AgendaItemCard, type DeltaBadge } from './AgendaItemCard';

interface Props {
  agenda: MayorAgenda | null;
  previous: MayorAgenda | null;
}

export function AgendaTab({ agenda, previous }: Props) {
  if (!agenda) {
    return (
      <div style={{ color: '#6b7280', padding: '2rem 0', textAlign: 'center' }}>
        Waiting for the first in-game day to roll over…
      </div>
    );
  }

  const previousShort = new Map<string, AgendaItem>(
    (previous?.short_term ?? []).map(i => [i.id, i] as const),
  );

  return (
    <div>
      <header style={{ marginBottom: '1.25rem' }}>
        <div style={{ fontSize: '0.85rem', color: '#6b7280', marginBottom: '0.25rem' }}>
          {agenda.updated_in_game_tick} · v{agenda.version}
        </div>
        <PostureRow posture={agenda.posture} />
      </header>

      <Section title="State of the Union">
        <p style={{ margin: 0, lineHeight: 1.55 }}>{agenda.state_of_the_union}</p>
      </Section>

      <Section title="What changed">
        <p style={{ margin: 0, fontStyle: 'italic', color: '#374151' }}>{agenda.update_notes}</p>
      </Section>

      <Section title="Short-term">
        {agenda.short_term.length === 0 && <Empty>No short-term priorities.</Empty>}
        {agenda.short_term.map((item, idx) => (
          <AgendaItemCard
            key={item.id}
            item={item}
            rank={idx + 1}
            delta={computeDelta(item, previousShort.get(item.id))}
          />
        ))}
      </Section>

      <Section title="Long-term">
        {agenda.long_term.length === 0 && <Empty>No long-term goals.</Empty>}
        {agenda.long_term.map(item => <LongTermRow key={item.id} item={item} />)}
      </Section>
    </div>
  );
}

function computeDelta(current: AgendaItem, prev: AgendaItem | undefined): DeltaBadge {
  if (!prev) return 'new';
  if (current.status === 'completed' && prev.status !== 'completed') return 'done';
  if (current.status === 'deferred' && prev.status !== 'deferred') return 'deferred';
  if (current.text !== prev.text) return 'updated';
  return null;
}

function PostureRow({ posture }: { posture: MayorAgenda['posture'] }) {
  return (
    <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
      <PostureBadge label={posture.economic} />
      <PostureBadge label={posture.military} />
      <span style={{ color: '#374151' }}>{posture.summary}</span>
    </div>
  );
}

function PostureBadge({ label }: { label: string }) {
  return (
    <span
      style={{
        fontSize: '0.75rem',
        fontWeight: 600,
        textTransform: 'capitalize',
        padding: '0.2rem 0.55rem',
        borderRadius: 12,
        background: '#eef2ff',
        color: '#3730a3',
      }}
    >
      {label}
    </span>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section style={{ marginBottom: '1.5rem' }}>
      <h3 style={{ fontSize: '0.8rem', textTransform: 'uppercase', letterSpacing: '0.06em',
                   color: '#6b7280', margin: '0 0 0.5rem' }}>{title}</h3>
      {children}
    </section>
  );
}

function Empty({ children }: { children: ReactNode }) {
  return <div style={{ color: '#9ca3af', fontStyle: 'italic' }}>{children}</div>;
}

function LongTermRow({ item }: { item: AgendaItem }) {
  const icon = item.status === 'completed' ? '✓' : item.status === 'deferred' ? '○' : '●';
  return (
    <div style={{ display: 'flex', gap: '0.6rem', padding: '0.3rem 0', color: '#374151' }}>
      <span style={{ width: '1rem', textAlign: 'center', color: '#6b7280' }}>{icon}</span>
      <span style={{ flex: 1, textDecoration: item.status === 'completed' ? 'line-through' : undefined }}>
        {item.text}
      </span>
    </div>
  );
}
