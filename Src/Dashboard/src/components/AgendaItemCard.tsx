import type { AgendaItem } from '../types/agenda';

export type DeltaBadge = 'new' | 'updated' | 'done' | 'deferred' | null;

interface Props {
  item: AgendaItem;
  delta: DeltaBadge;
  rank?: number;
}

const badgeColours: Record<Exclude<DeltaBadge, null>, { bg: string; fg: string }> = {
  new:      { bg: '#dcfce7', fg: '#166534' },
  updated:  { bg: '#fef3c7', fg: '#92400e' },
  done:     { bg: '#e0e7ff', fg: '#3730a3' },
  deferred: { bg: '#f3f4f6', fg: '#6b7280' },
};

export function AgendaItemCard({ item, delta, rank }: Props) {
  const isClosed = item.status !== 'active';
  return (
    <div
      style={{
        border: '1px solid #e5e7eb',
        borderRadius: 8,
        padding: '0.85rem 1rem',
        marginBottom: '0.6rem',
        background: '#fff',
        opacity: isClosed ? 0.65 : 1,
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '0.5rem' }}>
        <div style={{ flex: 1 }}>
          {rank !== undefined && (
            <span style={{ color: '#9ca3af', fontWeight: 500, marginRight: '0.4rem' }}>{rank}.</span>
          )}
          <span style={{ textDecoration: isClosed ? 'line-through' : undefined }}>{item.text}</span>
        </div>
        {delta && (
          <span
            style={{
              fontSize: '0.7rem',
              fontWeight: 600,
              letterSpacing: '0.05em',
              padding: '0.15rem 0.45rem',
              borderRadius: 4,
              textTransform: 'uppercase',
              background: badgeColours[delta].bg,
              color: badgeColours[delta].fg,
              whiteSpace: 'nowrap',
            }}
          >
            {delta}
          </span>
        )}
      </div>
      <div style={{ marginTop: '0.6rem', display: 'flex', gap: '0.4rem' }}>
        <FeedbackButton label="Accept" />
        <FeedbackButton label="Modify" />
        <FeedbackButton label="Dismiss" />
      </div>
    </div>
  );
}

function FeedbackButton({ label }: { label: string }) {
  return (
    <button
      disabled
      title="Wired in M2"
      style={{
        fontSize: '0.8rem',
        padding: '0.25rem 0.7rem',
        border: '1px solid #d1d5db',
        background: '#f9fafb',
        color: '#9ca3af',
        borderRadius: 4,
        cursor: 'not-allowed',
      }}
    >
      {label}
    </button>
  );
}
