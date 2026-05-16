import type { ScopeKey } from '../dashboard/scopes';
import type { MinisterTrace } from '../types/system';
import { postJson, readJson } from './http';

export interface PromptPayload {
  system: string;
  user: string;
}

export interface RawLlmOutputPayload {
  minister: string;
  provider: string;
  model: string;
  apiKeyIndex: number | null;
  apiKeyLabel: string | null;
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

export async function fetchBriefing(scope: ScopeKey, signal?: AbortSignal): Promise<unknown> {
  return await readJson<unknown>(`/api/briefings/${scope}/latest`, signal);
}

export async function fetchPrompt(scope: ScopeKey, signal?: AbortSignal): Promise<PromptPayload> {
  return await readJson<PromptPayload>(`/api/ministers/${scope}/prompt`, signal);
}

export async function fetchRawLlmOutput(scope: ScopeKey, signal?: AbortSignal): Promise<RawLlmOutputPayload> {
  return await readJson<RawLlmOutputPayload>(`/api/ministers/${scope}/llm-output/latest`, signal);
}

export async function fetchTrace(scope: ScopeKey, signal?: AbortSignal): Promise<MinisterTrace> {
  return await readJson<MinisterTrace>(`/api/ministers/${scope}/trace/latest`, signal);
}

export async function triggerMinister(scope: ScopeKey, signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>(`/api/ministers/${scope}/trigger`, signal);
}
