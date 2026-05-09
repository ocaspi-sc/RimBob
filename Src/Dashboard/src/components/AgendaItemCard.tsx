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
        {/* TODO: render item.cite_ids as guide footnotes once citation UI lands. */}
      </div>

      <div className="agenda-card-actions">
        {delta && (
          <span className={`delta-badge ${delta}`}>
            {delta}
          </span>
        )}
        {!isClosed && (
          <button
            className="pushback-button"
            disabled
            title="Wired in M2"
          >
            Pushback
          </button>
        )}
      </div>
    </div>
  );
}
