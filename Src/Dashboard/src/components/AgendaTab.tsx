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
        Waiting for the first briefing… (Mayor is calling Gemini.)
      </div>
    );
  }

  const previousShort = new Map<string, AgendaItem>(
    (previous?.short_term ?? []).map(i => [i.id, i] as const),
  );

  const ministerEntries = Object.entries(agenda.minister_direction ?? {});
  const activeShort = agenda.short_term.filter(i => i.status === 'active');
  const closedShort = agenda.short_term.filter(i => i.status !== 'active');

  return (
    <div>
      <AgendaHeader agenda={agenda} />

      <Section title="State of the Union" accent="#3730a3">
        <StateOfTheUnion entries={agenda.state_of_the_union} />
      </Section>

      <Section title="What changed" accent="#92400e">
        <p style={{ margin: 0, fontStyle: 'italic', color: '#374151', lineHeight: 1.55 }}>
          {agenda.update_notes}
        </p>
      </Section>

      <Section
        title="Short-term priorities"
        accent="#15803d"
        count={activeShort.length}
        countLabel="active"
      >
        {agenda.short_term.length === 0 && <Empty>No short-term priorities.</Empty>}
        {activeShort.map((item, idx) => (
          <AgendaItemCard
            key={item.id}
            item={item}
            rank={idx + 1}
            delta={computeDelta(item, previousShort.get(item.id))}
          />
        ))}
        {closedShort.length > 0 && (
          <details style={{ marginTop: '0.6rem' }}>
            <summary style={{ cursor: 'pointer', color: '#6b7280', fontSize: '0.85rem' }}>
              {closedShort.length} closed this turn
            </summary>
            <div style={{ marginTop: '0.4rem' }}>
              {closedShort.map(item => (
                <AgendaItemCard
                  key={item.id}
                  item={item}
                  delta={computeDelta(item, previousShort.get(item.id))}
                />
              ))}
            </div>
          </details>
        )}
      </Section>

      <Section
        title="Long-term goals"
        accent="#0e7490"
        count={agenda.long_term.length}
      >
        {agenda.long_term.length === 0 && <Empty>No long-term goals.</Empty>}
        {agenda.long_term.length > 0 && (
          <div style={{
            border: '1px solid #e5e7eb',
            borderRadius: 8,
            background: '#fff',
            overflow: 'hidden',
          }}>
            {agenda.long_term.map((item, i) => (
              <LongTermRow key={item.id} item={item} divider={i > 0} />
            ))}
          </div>
        )}
      </Section>

      {ministerEntries.length > 0 && (
        <Section title="Minister direction" accent="#7c3aed" count={ministerEntries.length}>
          <div style={{ display: 'grid', gap: '0.5rem' }}>
            {ministerEntries.map(([minister, direction]) => (
              <div
                key={minister}
                style={{
                  border: '1px solid #e5e7eb',
                  borderRadius: 8,
                  padding: '0.6rem 0.85rem',
                  background: '#fff',
                }}
              >
                <div style={{ fontSize: '0.75rem', fontWeight: 600, textTransform: 'uppercase',
                              letterSpacing: '0.05em', color: '#7c3aed', marginBottom: '0.2rem' }}>
                  {minister}
                </div>
                <div style={{ color: '#374151', fontSize: '0.9rem' }}>{direction}</div>
              </div>
            ))}
          </div>
        </Section>
      )}
    </div>
  );
}

function AgendaHeader({ agenda }: { agenda: MayorAgenda }) {
  return (
    <header
      style={{
        marginBottom: '1.5rem',
        padding: '1rem 1.1rem',
        background: 'linear-gradient(135deg, #f5f3ff 0%, #eef2ff 100%)',
        border: '1px solid #e0e7ff',
        borderRadius: 10,
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between',
                    alignItems: 'baseline', marginBottom: '0.6rem', flexWrap: 'wrap', gap: '0.5rem' }}>
        <div style={{ display: 'flex', gap: '0.4rem', alignItems: 'center', flexWrap: 'wrap' }}>
          <PostureBadge label={agenda.posture.economic} kind="economic" />
          <span style={{ color: '#9ca3af' }}>·</span>
          <PostureBadge label={agenda.posture.military} kind="military" />
        </div>
        <div style={{ fontSize: '0.8rem', color: '#6b7280', fontVariantNumeric: 'tabular-nums' }}>
          v{agenda.version} · {agenda.updated_in_game_tick}
        </div>
      </div>
      <div style={{ color: '#1f2937', lineHeight: 1.5, fontSize: '0.95rem' }}>
        {agenda.posture.summary}
      </div>
    </header>
  );
}

function PostureBadge({ label, kind }: { label: string; kind: 'economic' | 'military' }) {
  const palette = kind === 'economic'
    ? { bg: '#dcfce7', fg: '#166534', icon: '$' }
    : { bg: '#fee2e2', fg: '#991b1b', icon: '⚔' };
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '0.3rem',
        fontSize: '0.78rem',
        fontWeight: 600,
        textTransform: 'capitalize',
        padding: '0.25rem 0.6rem',
        borderRadius: 999,
        background: palette.bg,
        color: palette.fg,
      }}
    >
      <span style={{ opacity: 0.7 }}>{palette.icon}</span>
      {label}
    </span>
  );
}

// ── State of the Union ──────────────────────────────────────────────────────

const ministerMeta: Record<string, { emoji: string; label: string; order: number }> = {
  agriculture:  { emoji: '🌾', label: 'Agriculture',  order: 1 },
  defense:      { emoji: '🛡️', label: 'Defense',      order: 2 },
  welfare:      { emoji: '❤️', label: 'Welfare',      order: 3 },
  construction: { emoji: '🔨', label: 'Construction', order: 4 },
  treasury:     { emoji: '💰', label: 'Treasury',     order: 5 },
  research:     { emoji: '🔬', label: 'Research',     order: 6 },
};

function StateOfTheUnion({ entries }: { entries: Record<string, string> }) {
  const keys = Object.keys(entries);
  if (keys.length === 0) {
    return <Empty>No state-of-the-union entries this turn.</Empty>;
  }

  // Order known categories first, then anything unexpected at the end.
  const sorted = keys.slice().sort((a, b) => {
    const oa = ministerMeta[a]?.order ?? 99;
    const ob = ministerMeta[b]?.order ?? 99;
    return oa - ob;
  });

  return (
    <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
      {sorted.map(key => {
        const meta = ministerMeta[key] ?? { emoji: '•', label: capitalise(key), order: 99 };
        return (
          <li key={key} style={{
            display: 'grid',
            gridTemplateColumns: '1.5rem 6rem 1fr',
            gap: '0.6rem',
            padding: '0.4rem 0',
            borderBottom: '1px solid #f3f4f6',
            alignItems: 'baseline',
          }}>
            <span style={{ fontSize: '1.05rem', textAlign: 'center' }} aria-hidden>{meta.emoji}</span>
            <span style={{
              fontSize: '0.78rem',
              fontWeight: 600,
              color: '#3730a3',
              textTransform: 'uppercase',
              letterSpacing: '0.04em',
            }}>
              {meta.label}
            </span>
            <span style={{ color: '#1f2937', lineHeight: 1.5 }}>{entries[key]}</span>
          </li>
        );
      })}
    </ul>
  );
}

function capitalise(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}

function computeDelta(current: AgendaItem, prev: AgendaItem | undefined): DeltaBadge {
  if (!prev) return 'new';
  if (current.status === 'completed' && prev.status !== 'completed') return 'done';
  if (current.status === 'deferred' && prev.status !== 'deferred') return 'deferred';
  if (current.text !== prev.text) return 'updated';
  return null;
}

interface SectionProps {
  title: string;
  accent: string;
  children: ReactNode;
  count?: number;
  countLabel?: string;
}

function Section({ title, accent, children, count, countLabel }: SectionProps) {
  return (
    <section style={{ marginBottom: '1.5rem' }}>
      <h3 style={{
        display: 'flex',
        alignItems: 'baseline',
        gap: '0.5rem',
        margin: '0 0 0.65rem',
        paddingLeft: '0.6rem',
        borderLeft: `3px solid ${accent}`,
        fontSize: '0.78rem',
        textTransform: 'uppercase',
        letterSpacing: '0.07em',
        color: '#374151',
        fontWeight: 600,
      }}>
        <span>{title}</span>
        {count !== undefined && (
          <span style={{ color: '#9ca3af', fontWeight: 500, letterSpacing: 0, textTransform: 'none' }}>
            {count}{countLabel ? ` ${countLabel}` : ''}
          </span>
        )}
      </h3>
      {children}
    </section>
  );
}

function Empty({ children }: { children: ReactNode }) {
  return <div style={{ color: '#9ca3af', fontStyle: 'italic', padding: '0.5rem 0' }}>{children}</div>;
}

function LongTermRow({ item, divider }: { item: AgendaItem; divider: boolean }) {
  const icon = item.status === 'completed' ? '✓' : item.status === 'deferred' ? '○' : '●';
  const iconColor =
    item.status === 'completed' ? '#15803d' :
    item.status === 'deferred'  ? '#9ca3af' : '#0e7490';

  return (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      gap: '0.7rem',
      padding: '0.7rem 0.9rem',
      borderTop: divider ? '1px solid #f3f4f6' : 'none',
      color: '#374151',
    }}>
      <span style={{ width: '1rem', textAlign: 'center', color: iconColor, fontSize: '0.85rem' }}>
        {icon}
      </span>
      <span style={{
        flex: 1,
        textDecoration: item.status === 'completed' ? 'line-through' : undefined,
        color: item.status === 'deferred' ? '#9ca3af' : '#1f2937',
      }}>
        {item.text}
      </span>
      <span style={{
        fontSize: '0.7rem',
        textTransform: 'uppercase',
        letterSpacing: '0.05em',
        color: iconColor,
        fontWeight: 600,
      }}>
        {item.status}
      </span>
    </div>
  );
}
