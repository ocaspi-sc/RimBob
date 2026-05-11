import type { ColonySnapshot } from '../types/colony';

export async function fetchColonySnapshot(signal?: AbortSignal): Promise<ColonySnapshot | null> {
  const res = await fetch('/api/colony/snapshot', { signal });
  if (res.status === 204) return null;
  if (!res.ok) throw new Error(`/api/colony/snapshot returned ${res.status}`);
  return await res.json() as ColonySnapshot;
}

export async function fetchLatestBriefing(minister: 'mayor' | 'food', signal?: AbortSignal): Promise<unknown> {
  const res = await fetch(`/api/briefings/${minister}/latest`, { signal });
  if (!res.ok) throw new Error(`/api/briefings/${minister}/latest returned ${res.status}`);
  return await res.json() as unknown;
}
