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
        padding: '0.85rem 1rem 0.7rem',
        marginBottom: '0.6rem',
        background: '#fff',
        opacity: isClosed ? 0.65 : 1,
        display: 'grid',
        gridTemplateColumns: rank !== undefined ? '2rem 1fr auto' : '1fr auto',
        columnGap: '0.75rem',
        rowGap: '0.6rem',
        alignItems: 'start',
      }}
    >
      {rank !== undefined && (
        <div
          style={{
            width: '1.9rem',
            height: '1.9rem',
            borderRadius: '50%',
            background: '#eef2ff',
            color: '#3730a3',
            fontWeight: 600,
            fontSize: '0.85rem',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0,
          }}
        >
          {rank}
        </div>
      )}

      <div style={{
        lineHeight: 1.5,
        color: '#1f2937',
        textDecoration: isClosed ? 'line-through' : undefined,
      }}>
        {item.text}
      </div>

      {delta && (
        <span
          style={{
            fontSize: '0.7rem',
            fontWeight: 600,
            letterSpacing: '0.05em',
            padding: '0.18rem 0.5rem',
            borderRadius: 4,
            textTransform: 'uppercase',
            background: badgeColours[delta].bg,
            color: badgeColours[delta].fg,
            whiteSpace: 'nowrap',
            justifySelf: 'end',
          }}
        >
          {delta}
        </span>
      )}

      <div style={{
        gridColumn: rank !== undefined ? '2 / -1' : '1 / -1',
        display: 'flex',
        gap: '0.4rem',
        paddingTop: '0.15rem',
      }}>
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
        fontSize: '0.78rem',
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
