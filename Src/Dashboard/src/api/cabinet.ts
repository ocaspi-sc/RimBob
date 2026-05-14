import type { ManualTriggerPayload } from './ministers';
import { postJson } from './http';

export async function triggerCabinet(signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>('/api/cabinet/trigger', signal);
}
