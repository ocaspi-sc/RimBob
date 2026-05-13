import type { MayorAgenda } from '../types/agenda';
import type { AdviceItem, AdviceSnapshot } from '../types/advice';
import type { ColonySnapshot } from '../types/colony';
import type { RimAIStatus } from '../types/status';
import type { MinisterTrace, SystemHealth } from '../types/system';
import type { ScopeKey } from '../dashboard/scopes';

export interface PromptPayload {
  system: string;
  user: string;
}

export interface RawLlmOutputPayload {
  minister: string;
  provider: string;
  model: string;
  capturedAt: string;
  latencyMs: number;
  status: string;
  parseMode: string;
  systemPromptChars: number;
  userPromptChars: number;
  text: string;
}

export interface ManualTriggerPayload {
  triggered: boolean;
  scope: string;
  minister?: string;
  trigger: string;
}

async function readJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(`${url} returned ${response.status}`);
  }
  return await response.json() as T;
}

async function postJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { method: 'POST', signal });
  if (!response.ok) {
    throw new Error(`${url} returned ${response.status}: ${await readErrorDetail(response)}`);
  }
  return await response.json() as T;
}

async function readErrorDetail(response: Response): Promise<string> {
  try {
    const body = await response.json() as { title?: string; detail?: string; error?: string };
    return body.detail ?? body.error ?? body.title ?? response.statusText;
  } catch {
    return response.statusText;
  }
}

export async function fetchStatus(signal?: AbortSignal): Promise<RimAIStatus> {
  return await readJson<RimAIStatus>('/api/status', signal);
}

export async function fetchSystemHealth(signal?: AbortSignal): Promise<SystemHealth> {
  return await readJson<SystemHealth>('/api/system/health', signal);
}

export async function fetchLatestAgenda(signal?: AbortSignal): Promise<MayorAgenda | null> {
  const response = await fetch('/api/agenda/latest', { signal });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`/api/agenda/latest returned ${response.status}`);
  return await response.json() as MayorAgenda;
}

export async function fetchColonySnapshot(signal?: AbortSignal): Promise<ColonySnapshot | null> {
  const response = await fetch('/api/colony/snapshot', { signal });
  if (response.status === 204) return null;
  if (!response.ok) throw new Error(`/api/colony/snapshot returned ${response.status}`);
  return await response.json() as ColonySnapshot;
}

export async function fetchBriefing(scope: ScopeKey, signal?: AbortSignal): Promise<unknown> {
  if (scope !== 'mayor' && scope !== 'food') {
    throw new Error(`${scope} briefing is not exposed yet`);
  }
  return await readJson<unknown>(`/api/briefings/${scope}/latest`, signal);
}

export async function fetchPrompt(scope: ScopeKey, signal?: AbortSignal): Promise<PromptPayload> {
  if (scope !== 'mayor' && scope !== 'food') {
    throw new Error(`${scope} prompt is not exposed yet`);
  }
  return await readJson<PromptPayload>(`/api/ministers/${scope}/prompt`, signal);
}

export async function fetchRawLlmOutput(scope: ScopeKey, signal?: AbortSignal): Promise<RawLlmOutputPayload> {
  if (scope !== 'mayor' && scope !== 'food') {
    throw new Error(`${scope} raw LLM output is not exposed yet`);
  }
  return await readJson<RawLlmOutputPayload>(`/api/ministers/${scope}/llm-output/latest`, signal);
}

export async function fetchTrace(scope: ScopeKey, signal?: AbortSignal): Promise<MinisterTrace> {
  return await readJson<MinisterTrace>(`/api/ministers/${scope}/trace/latest`, signal);
}

export async function triggerCabinet(signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>('/api/cabinet/trigger', signal);
}

export async function triggerMinister(scope: ScopeKey, signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>(`/api/ministers/${scope}/trigger`, signal);
}

export function parseAgendaEvent(event: MessageEvent): MayorAgenda {
  return JSON.parse(event.data) as MayorAgenda;
}

export function parseAdviceEvent(event: MessageEvent): AdviceItem {
  return JSON.parse(event.data) as AdviceItem;
}

export function parseAdviceSnapshotEvent(event: MessageEvent): AdviceSnapshot {
  return JSON.parse(event.data) as AdviceSnapshot;
}
