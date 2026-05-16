import type { IconRef } from '../types/icons';

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
