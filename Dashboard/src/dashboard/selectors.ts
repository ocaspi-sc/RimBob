import type { ScopeConfig } from './scopes';
import type { SystemHealth } from '../types/system';

export function formatLastRun(scope: ScopeConfig, health: SystemHealth | null): string {
  if (scope.kind !== 'minister') return '';
  if (scope.status !== 'live') return 'Not wired';
  if (!health) return 'Last run loading...';

  const trace = health.traces.find(item => isScopeMinister(item.minister, scope));

  if (!trace) return formatPersistedOutput(scope, health) ?? 'Last run never';
  const timestamp = trace.completedAt ?? trace.startedAt;
  const label = trace.status === 'running' ? 'Running since' : 'Last run';
  return `${label} ${formatTraceTime(timestamp)}`;
}

function formatPersistedOutput(scope: ScopeConfig, health: SystemHealth): string | null {
  const snapshot = health.minister_outputs.snapshots.find(item => isScopeMinister(item.minister, scope));
  if (!snapshot) return null;
  if (snapshot.persisted_at) return `Last output ${formatTraceTime(snapshot.persisted_at)}`;
  if (snapshot.state && snapshot.state !== 'available') return `Snapshot ${snapshot.state.replace(/_/g, ' ')}`;
  return null;
}

export function isScopeMinister(minister: string | null | undefined, scope: ScopeConfig): boolean {
  if (!minister) return false;
  const normalized = normalizeMinisterReference(minister);
  return ministerAliases(scope).some(alias => normalizeMinisterReference(alias) === normalized);
}

export function valueForScope<T>(values: Record<string, T>, scope: ScopeConfig): T | undefined {
  for (const alias of ministerAliases(scope)) {
    const exact = values[alias];
    if (exact !== undefined) return exact;
  }

  return Object.entries(values).find(([key]) => isScopeMinister(key, scope))?.[1];
}

function ministerAliases(scope: ScopeConfig): string[] {
  if (scope.key === 'food') return [scope.key, scope.label, 'Food', 'Chef', 'chef'];
  return [scope.key, scope.label];
}

function normalizeMinisterReference(value: string): string {
  return value.trim().replace(/[\s-]+/g, '_').toLocaleLowerCase();
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
