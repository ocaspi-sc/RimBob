import type { ManualTriggerPayload } from './ministers';
import { postJson } from './http';

export async function triggerCabinet(runId: string, signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>('/api/cabinet/trigger', signal, { run_id: runId });
}

export async function triggerCabinetRules(runId: string, signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>('/api/cabinet/trigger/rules', signal, { run_id: runId });
}
