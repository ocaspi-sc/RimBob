import { postJson } from './http';
import type { AdviceApplyResponse } from '../types/advice';

export function applyAdviceStep(
  adviceId: string,
  stepIndex: number,
  signal?: AbortSignal,
): Promise<AdviceApplyResponse> {
  return postJson<AdviceApplyResponse>(
    `/api/advice/${encodeURIComponent(adviceId)}/steps/${stepIndex}/apply`,
    signal,
  );
}
