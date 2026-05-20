import type { ColonySnapshot } from '../types/colony';
import { timedFetch } from './http';

export async function fetchColonySnapshot(signal?: AbortSignal): Promise<ColonySnapshot | null> {
  const response = await timedFetch('/api/colony/snapshot', { signal });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`/api/colony/snapshot returned ${response.status}`);

  return await response.json() as ColonySnapshot;
}
