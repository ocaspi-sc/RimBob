import type { RimAIStatus } from '../types/status';

export async function fetchStatus(signal?: AbortSignal): Promise<RimAIStatus> {
  const res = await fetch('/api/status', { signal });
  if (!res.ok) throw new Error(`fetchStatus failed: ${res.status}`);
  return (await res.json()) as RimAIStatus;
}

export async function fetchMayorPrompt(): Promise<{ system: string; user: string }> {
  const res = await fetch('/api/mayor/prompt');
  if (!res.ok) throw new Error(`fetchMayorPrompt failed: ${res.status}`);
  return res.json() as Promise<{ system: string; user: string }>;
}
