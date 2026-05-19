import type { IconRef } from '../types/icons';
import type { IconCacheStatus, IconWarmJobStatus } from '../types/icons';
import { postJson, readJson } from './http';

export function iconUrlFor(ref: IconRef | null | undefined): string | null {
  if (!ref || !ref.id) return null;

  switch (ref.kind) {
    case 'item':
      return itemIconUrl(ref.id);
    case 'terrain':
      return terrainIconUrl(ref.id);
    case 'faction':
      return factionIconUrl(ref.id);
    case 'pawn':
      return pawnPortraitUrl(ref.id);
    default:
      return null;
  }
}

export function itemIconUrl(defName: string | null | undefined): string | null {
  if (!defName) return null;
  return `/api/icons/item/${encodeURIComponent(defName)}`;
}

export function terrainIconUrl(defName: string | null | undefined): string | null {
  if (!defName) return null;
  return `/api/icons/terrain/${encodeURIComponent(defName)}`;
}

export function factionIconUrl(loadId: string | number | null | undefined): string | null {
  if (loadId === null || loadId === undefined || loadId === '') return null;
  return `/api/icons/faction/${encodeURIComponent(String(loadId))}`;
}

export function pawnPortraitUrl(
  pawnId: string | number | null | undefined,
  width = 96,
  height = 96,
  direction = 'South',
): string | null {
  if (pawnId === null || pawnId === undefined || pawnId === '') return null;
  return `/api/icons/pawn/${encodeURIComponent(String(pawnId))}/portrait?width=${width}&height=${height}&direction=${encodeURIComponent(direction)}`;
}

export async function startIconCacheWarm(
  scope: 'all' | 'failed' = 'all',
  signal?: AbortSignal,
): Promise<IconWarmJobStatus> {
  const suffix = scope === 'failed' ? '?scope=failed' : '';
  return await postJson<IconWarmJobStatus>(`/api/icons/cache/warm${suffix}`, signal);
}

export async function fetchIconCacheStatus(includeFiles = false, signal?: AbortSignal): Promise<IconCacheStatus> {
  const suffix = includeFiles ? '?includeFiles=true' : '';
  return await readJson<IconCacheStatus>(`/api/icons/cache/status${suffix}`, signal);
}
