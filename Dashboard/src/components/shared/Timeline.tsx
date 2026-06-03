import { displayMinisterName } from '../../dashboard/scopes';
import type { DashboardEvent } from '../../types/system';

export function Timeline({ events, limit = 10 }: { events: DashboardEvent[]; limit?: number }) {
  const visible = events.slice(0, limit);

  if (visible.length === 0) {
    return <div className="muted-row">No events recorded in this dashboard session.</div>;
  }

  return (
    <div className="timeline">
      {visible.map(event => (
        <div className={`timeline-row ${event.severity}`} key={event.id}>
          <time>{formatTime(event.at)}</time>
          <strong>{displayMinisterName(event.source)}</strong>
          <span>{event.type}</span>
          <p>{event.summary}</p>
        </div>
      ))}
    </div>
  );
}

function formatTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}
