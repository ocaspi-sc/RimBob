import type { RimAIStatus } from '../types/status';
import type { SystemHealth } from '../types/system';
import { readJson } from './http';

export async function fetchStatus(signal?: AbortSignal): Promise<RimAIStatus> {
  return await readJson<RimAIStatus>('/api/status', signal);
}

export async function fetchSystemHealth(signal?: AbortSignal): Promise<SystemHealth> {
  return await readJson<SystemHealth>('/api/system/health', signal);
}
