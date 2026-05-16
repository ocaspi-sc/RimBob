import type { IconRef } from './icons';

export type AdvicePriority = 'low' | 'medium' | 'high' | 'critical';

export interface ResourceRequest {
  kind: string;
  request: string;
  reason: string;
  quantity?: number | null;
  priority?: AdvicePriority | null;
  requested_from?: string | null;
  work_type?: string | null;
  skill?: string | null;
  icon?: IconRef | null;
}

export interface SuggestedAction {
  kind: string;
  instruction: string;
  icon?: IconRef | null;
}

export interface AdviceItem {
  id: string;
  minister: string;
  advice_type: string;
  priority: AdvicePriority;
  title: string;
  body: string;
  rationale: string;
  resource_requests: ResourceRequest[];
  suggested_actions: SuggestedAction[];
  guide_citations: string[];
  issued_at: string;
  expires_at: string;
  issued_in_game_tick?: string | null;
}

export interface AdviceSnapshot {
  minister?: string | null;
  advice: AdviceItem[];
  state_summary?: string | null;
  state_summaries?: Record<string, string> | null;
}
