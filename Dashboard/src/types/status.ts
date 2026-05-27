export interface RimWorldRuntimeStatus {
  live: boolean;
  program_state: string | null;
  map_count: number | null;
  colonist_count: number | null;
  game_tick: number | null;
  is_paused: boolean | null;
}

export interface RimBobStatus {
  server: string;
  rimapi_reachable: boolean;
  rimapi_last_error?: string | null;
  rimworld?: RimWorldRuntimeStatus;
  colony_state_origin?: string;
  last_live_refresh_at?: string | null;
  llm_configured: boolean;
  llm_status: string;
  llm_last_event_at: string | null;
  llm_last_error: string | null;
  briefing_version: number;
  mayor_snapshot_version: number | null;
  mayor_running: boolean;
  mayor_started_at: string | null;   // ISO 8601 UTC
  mayor_completed_at: string | null; // ISO 8601 UTC
  mayor_last_llm_success_at?: string | null;
  mayor_last_error: string | null;
}
