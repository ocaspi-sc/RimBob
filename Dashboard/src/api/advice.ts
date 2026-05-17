import { postJson } from './http';
import type { AdviceApplyResponse } from '../types/advice';

export function applyAdviceAction(
  adviceId: string,
  actionIndex: number,
  signal?: AbortSignal,
): Promise<AdviceApplyResponse> {
  return postJson<AdviceApplyResponse>(
    `/api/advice/${encodeURIComponent(adviceId)}/actions/${actionIndex}/apply`,
    signal,
  );
}
