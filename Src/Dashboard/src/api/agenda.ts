import type { MayorAgenda } from '../types/agenda';

export async function fetchLatestAgenda(): Promise<MayorAgenda | null> {
  const res = await fetch('/api/agenda/latest');
  if (res.status === 204) return null;
  if (!res.ok) throw new Error(`fetchLatestAgenda failed: ${res.status} ${res.statusText}`);
  return (await res.json()) as MayorAgenda;
}
