import type { AdviceItem, AdviceSnapshot } from '../types/advice';

export function parseAdviceEvent(event: MessageEvent): AdviceItem {
  return JSON.parse(event.data) as AdviceItem;
}

export function parseAdviceSnapshotEvent(event: MessageEvent): AdviceSnapshot {
  return JSON.parse(event.data) as AdviceSnapshot;
}
