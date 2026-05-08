import type { ColonySnapshot } from '../types/colony';

export async function fetchColonySnapshot(signal?: AbortSignal): Promise<ColonySnapshot | null> {
  const res = await fetch('/api/colony/snapshot', { signal });
  if (res.status === 204) return null;
  if (!res.ok) throw new Error(`/api/colony/snapshot returned ${res.status}`);
  return await res.json() as ColonySnapshot;
}
