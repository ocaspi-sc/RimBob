import type { AdviceItem, AdviceSnapshot } from '../types/advice';
import type { RimBobRunningVersion } from '../types/system';

export function parseHostReadyEvent(event: MessageEvent): RimBobRunningVersion {
  return JSON.parse(event.data) as RimBobRunningVersion;
}

export function parseAdviceEvent(event: MessageEvent): AdviceItem {
  return JSON.parse(event.data) as AdviceItem;
}

export function parseAdviceSnapshotEvent(event: MessageEvent): AdviceSnapshot {
  return JSON.parse(event.data) as AdviceSnapshot;
}
