export interface RimAIStatus {
  server: string;
  rimapi_reachable: boolean;
  llm_configured: boolean;
  briefing_version: number;
  agenda_version: number | null;
  mayor_running: boolean;
  mayor_started_at: string | null;   // ISO 8601 UTC
  mayor_completed_at: string | null; // ISO 8601 UTC
  mayor_last_error: string | null;
}
