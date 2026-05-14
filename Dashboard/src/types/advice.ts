export type AdviceSeverity = 'low' | 'medium' | 'high' | 'critical';

export interface ResourceRequest {
  kind: string;
  what: string;
  why: string;
  quantity?: number | null;
  priority?: AdviceSeverity | null;
  requested_from?: string | null;
  work_type?: string | null;
  skill?: string | null;
}

export interface SuggestedAction {
  kind: string;
  what: string;
}

export interface AdviceItem {
  id: string;
  minister: string;
  advice_type: string;
  severity: AdviceSeverity;
  priority_score: number;
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
}
