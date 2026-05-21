import type { AdviceChainModel, AdviceItem, AgentFlag } from './advice';
import type { AdviceApplyKind } from './advice';
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

export interface RimBobRunningVersion {
  product: string;
  rim_bob_version: string;
  running_version: string;
  build_number: string;
  build_datetime: string;
  build_version: string;
  build_informational_version: string;
  build_revision: string | null;
  build_revision_short: string | null;
  dashboard_asset_version: string;
  reload_token: string;
  host_started_at: string;
  host_instance_id: string;
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

export interface TestCategorySummary {
  category: string;
  count: number;
  file_count: number;
}

export interface TestInventoryMetadata {
  directory: string;
  project: string;
  exists: boolean;
  total_count: number;
  file_count: number;
  source: string;
  scan_error: string | null;
  categories: TestCategorySummary[];
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

export interface ColonySnapshotMetadata {
  path: string | null;
  has_snapshot: boolean;
  snapshot_id: string | null;
  captured_at: string | null;
  age_seconds: number | null;
  game_tick: number | null;
  map_id: number | null;
  source: string | null;
  schema_version: number | null;
  last_save_at: string | null;
  last_save_error: string | null;
  load_error: string | null;
}

export interface MinisterOutputSnapshotMetadata {
  minister: string;
  output_kind: string;
  path: string | null;
  generation: number | null;
  persisted_at: string | null;
  state: string;
  last_error: string | null;
}

export interface MinisterOutputStoreMetadata {
  root_path: string | null;
  exists: boolean;
  snapshots: MinisterOutputSnapshotMetadata[];
}

export interface MinisterTrace {
  minister: string;
  trigger: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  path: string;
  ruleFired: string | null;
  ruleDiagnostics: RuleTraceDetails | null;
  escalationReason: string | null;
  errorType: string | null;
  errorMessage: string | null;
  adviceCount: number | null;
  flagCount: number | null;
  wakeupPayload: string | null;
  flag: unknown | null;
  note: string;
}

export interface RuleTraceDetails {
  selectedRule: string | null;
  matchedSignals: RuleTraceEntry[];
  suppressedCandidates: RuleTraceEntry[];
  allRules?: RuleEvaluationTrace[];
  emittedAdvice?: RuleEmittedAdviceTrace[];
  emittedActions?: RuleEmittedActionTrace[];
  emittedFlags?: RuleEmittedFlagTrace[];
}

export interface RuleTraceEntry {
  rule: string;
  outcome: string;
  reason: string;
}

export interface RuleEvaluationTrace {
  rule: string;
  outcome: string;
  conditions: string;
  outputAction: string;
  reason: string | null;
}

export interface RuleEmittedAdviceTrace {
  source: string;
  rule: string;
  adviceId: string;
  adviceType: string;
  priority: string;
  title: string;
  actionCount: number;
}

export interface RuleEmittedActionTrace {
  source: string;
  rule: string;
  adviceId: string;
  actionIndex: number;
  kind: string;
  instruction: string;
  reason: string | null;
  applyKind: string | null;
  applyLabel: string | null;
  applyTargetSummary: string | null;
}

export interface RuleEmittedFlagTrace {
  source: string;
  rule: string;
  flagId: string;
  severity: string;
  summary: string;
  requestCount: number;
}

export interface AssistedApplyAttempt {
  at: string;
  status: string;
  message: string;
  kind?: AdviceApplyKind | null;
  advice_id: string;
  action_index: number;
}

export interface SystemHealth {
  generated_at: string;
  version: RimBobRunningVersion;
  runtime: {
    server: string;
    host_started_at: string;
    host_instance_id: string;
    rim_bob_version: string;
    running_version: string;
    build_number: string;
    build_datetime: string;
    build_version: string;
    build_informational_version: string;
    dashboard_asset_version: string;
    host_process_path: string;
    content_root: string;
    runtime_root: string;
    rimapi_reachable: boolean;
    colony_state_origin: string;
    last_live_refresh_at: string | null;
    briefing_version: number;
    food_briefing_version: number;
    mayor_snapshot_version: number | null;
    active_advice_count: number;
    active_flag_count: number;
    mayor_running: boolean;
    mayor_started_at: string | null;
    mayor_completed_at: string | null;
    mayor_last_llm_success_at: string | null;
    mayor_last_error: string | null;
  };
  storage: {
    data_root: string;
    minister_output_root: string;
    colony_state_snapshot_path: string;
    embedding_cache_root: string;
  };
  minister_outputs: MinisterOutputStoreMetadata;
  colony_snapshot: ColonySnapshotMetadata;
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
  tests: TestInventoryMetadata;
  icons: IconCacheStatus;
  traces: MinisterTrace[];
  assisted_apply?: {
    recent_attempts: AssistedApplyAttempt[];
  };
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
  chains: Record<string, AdviceChainModel>;
  flags: Record<string, AgentFlag[]>;
  stateSummaries: Record<string, string>;
}
