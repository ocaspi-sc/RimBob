import type { ScopeConfig } from './scopes';
import type { SystemHealth } from '../types/system';

export function formatLastRun(scope: ScopeConfig, health: SystemHealth | null): string {
  if (scope.kind !== 'minister') return '';
  if (scope.status !== 'live') return 'Not wired';

  const trace = health?.traces.find(item =>
    item.minister.localeCompare(scope.label, undefined, { sensitivity: 'accent' }) === 0);

  if (!trace) return 'Last run never';
  const timestamp = trace.completedAt ?? trace.startedAt;
  const label = trace.status === 'running' ? 'Running since' : 'Last run';
  return `${label} ${formatTraceTime(timestamp)}`;
}

function formatTraceTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const now = new Date();
  const sameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();

  return sameDay
    ? date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}
