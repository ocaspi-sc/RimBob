import type { MayorAgenda } from '../types/agenda';
import { timedFetch } from './http';

export async function fetchLatestAgenda(signal?: AbortSignal): Promise<MayorAgenda | null> {
  const response = await timedFetch('/api/ministers/mayor/snapshot', { signal });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`/api/ministers/mayor/snapshot returned ${response.status}`);

  return await response.json() as MayorAgenda;
}

export function parseAgendaEvent(event: MessageEvent): MayorAgenda {
  return JSON.parse(event.data) as MayorAgenda;
}
