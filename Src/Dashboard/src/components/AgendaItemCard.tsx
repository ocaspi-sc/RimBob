import type { AgendaItem } from '../types/agenda';

export type DeltaBadge = 'new' | 'updated' | 'done' | 'deferred' | null;

interface Props {
  item: AgendaItem;
  delta: DeltaBadge;
  rank?: number;
}

export function AgendaItemCard({ item, delta, rank }: Props) {
  const isClosed = item.status !== 'active';
  return (
    <div className={`agenda-card ${isClosed ? 'closed' : ''} ${rank !== undefined ? 'ranked' : ''}`}>
      {rank !== undefined && (
        <div className="rank-token">
          {rank}
        </div>
      )}

      <div className="agenda-card-text">
        {item.text}
      </div>

      {delta && (
        <span className={`delta-badge ${delta}`}>
          {delta}
        </span>
      )}

      <div className="feedback-row">
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
      className="feedback-button"
    >
      {label}
    </button>
  );
}
