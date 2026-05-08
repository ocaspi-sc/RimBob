import type { MayorAgenda } from '../types/agenda';

export async function fetchLatestAgenda(): Promise<MayorAgenda | null> {
  const res = await fetch('/api/agenda/latest');
  if (res.status === 204) return null;
  if (!res.ok) throw new Error(`fetchLatestAgenda failed: ${res.status} ${res.statusText}`);
  return (await res.json()) as MayorAgenda;
}

export async function triggerRefresh(signal?: AbortSignal): Promise<void> {
  const res = await fetch('/api/agenda/refresh', { method: 'POST', signal });
  if (!res.ok) throw new Error(`triggerRefresh failed: ${res.status} ${res.statusText}`);
}
