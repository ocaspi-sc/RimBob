import type { MayorAgenda } from '../types/agenda';

export async function fetchLatestAgenda(signal?: AbortSignal): Promise<MayorAgenda | null> {
  const response = await fetch('/api/agenda/latest', { signal });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`/api/agenda/latest returned ${response.status}`);

  return await response.json() as MayorAgenda;
}

export function parseAgendaEvent(event: MessageEvent): MayorAgenda {
  return JSON.parse(event.data) as MayorAgenda;
}
