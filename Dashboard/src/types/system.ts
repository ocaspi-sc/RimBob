import type { AdviceItem } from './advice';
import type { IconCacheStatus } from './icons';

export type CoverageState = 'available' | 'missing' | 'failed' | 'unsupported' | 'stale' | 'partial' | 'not_exposed_yet';

export interface EndpointCoverage {
  endpoint: string;
  state: CoverageState | string;
  note: string;
}

export interface SseHealth {
  activeConnections: number;
  totalConnections: number;
  eventCount: number;
  errorCount: number;
  lastConnectedAt: string | null;
  lastDisconnectedAt: string | null;
  lastEventAt: string | null;
  lastEventType: string | null;
  lastEventId: string | null;
  lastErrorAt: string | null;
  lastError: string | null;
}

export interface ReplayCorpusFile {
  minister: string;
  name: string;
  path: string;
  size_bytes: number;
  last_write_at: string;
}

export interface ReplayCorpusMetadata {
  directory: string;
  pattern: string;
  exists: boolean;
  file_count: number;
  total_bytes: number;
  latest_write_at: string | null;
  files: ReplayCorpusFile[];
}

export interface RimApiCoverageRow {
  method: string;
  endpoint: string;
  state: string;
  owner: string;
  note: string;
}

export interface RimApiCoverageMetadata {
  coverage_basis: string;
  source: string;
  cached_upstream_endpoint_total: number;
  active_read_count: number;
  client_method_count: number;
  deferred_write_stub_count: number;
  represented_endpoint_count: number;
  active_read_percent: number;
  represented_endpoint_percent: number;
  coverage_note: string;
  active_reads: RimApiCoverageRow[];
  represented_not_refreshed: RimApiCoverageRow[];
  deferred_writes: RimApiCoverageRow[];
  missing_priorities: RimApiCoverageRow[];
}

export interface MinisterTrace {
  minister: string;
  trigger: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  path: string;
  ruleFired: string | null;
  escalationReason: string | null;
  wakeupPayload: string | null;
  flag: unknown | null;
  note: string;
}

export interface SystemHealth {
  generated_at: string;
  runtime: {
    server: string;
    rimapi_reachable: boolean;
    briefing_version: number;
    food_briefing_version: number;
    agenda_version: number | null;
    active_advice_count: number;
    active_flag_count: number;
    mayor_running: boolean;
    mayor_started_at: string | null;
    mayor_completed_at: string | null;
    mayor_last_llm_success_at: string | null;
    mayor_last_error: string | null;
  };
  llm: {
    provider: string;
    configured: boolean;
    configured_key_count: number;
    status: string;
    last_event_at: string | null;
    last_success_at: string | null;
    last_error: string | null;
    token_usage: string;
  };
  rag: {
    enabled: boolean;
    top_k: number;
    embedding_model: string;
    guides_root: string;
    cache_root: string;
    chunk_count: number;
    last_ingest_error: string;
  };
  sse: SseHealth;
  logs: {
    directory: string;
    human_log_pattern: string;
    decision_log_pattern: string;
    replay_corpus: ReplayCorpusMetadata;
    recent_endpoint: string;
  };
  icons: IconCacheStatus;
  traces: MinisterTrace[];
  endpoint_coverage: EndpointCoverage[];
  rimapi_coverage: RimApiCoverageMetadata;
}

export interface DashboardEvent {
  id: string;
  at: string;
  source: string;
  type: string;
  severity: 'info' | 'warn' | 'error';
  summary: string;
}

export interface StreamDiagnostics {
  state: 'connecting' | 'open' | 'closed' | 'error';
  readyState: number;
  eventCount: number;
  reconnectCount: number;
  lastEventType: string | null;
  lastEventId: string | null;
  lastEventAt: string | null;
  lastErrorAt: string | null;
}

export interface FeedState {
  agendaEvents: number;
  adviceEvents: number;
  activeAdvice: AdviceItem[];
  stateSummaries: Record<string, string>;
}
