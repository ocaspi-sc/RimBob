import type { PillTone } from '../shared/StatusPill';

export function readinessTone(value: string | null | undefined): PillTone {
  const normalized = (value ?? '').toLowerCase();
  if (['ready', 'valid', 'available', 'true', 'ok'].includes(normalized)) return 'ok';
  if (['blocked', 'invalid', 'failed', 'false', 'not_ready'].includes(normalized)) return 'error';
  if (normalized.includes('missing') || normalized.includes('unsupported') || normalized.includes('not_exposed')) return 'warn';
  return 'idle';
}
