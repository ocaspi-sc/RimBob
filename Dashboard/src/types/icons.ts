export type IconKind = 'item' | 'terrain' | 'faction' | 'pawn' | string;

export interface IconRef {
  kind: IconKind;
  id: string;
}

export interface IconWarmFailure {
  kind: string;
  id: string;
  status: 'failed' | 'deferred' | string;
  error: string;
  failureKind: 'missing' | 'transport' | 'rimapi_error' | 'invalid_response' | 'bad_key' | 'deferred' | string | null;
}

export interface IconWarmSummary {
  startedAt: string;
  completedAt: string;
  totalCandidates: number;
  succeeded: number;
  failed: number;
  deferred: number;
  skipped: number;
  itemCandidates: number;
  terrainCandidates: number;
  factionCandidates: number;
  missingFailures: number;
  transportFailures: number;
  failures: IconWarmFailure[];
}

export interface IconWarmJobStatus {
  state: 'idle' | 'running' | 'completed' | 'failed' | string;
  jobId: string | null;
  scope: 'all' | 'failed' | string;
  startedAt: string | null;
  updatedAt: string | null;
  completedAt: string | null;
  total: number;
  done: number;
  succeeded: number;
  failed: number;
  deferred: number;
  error: string | null;
}

export interface IconCacheFile {
  kind: string;
  id: string;
  name: string;
  relativePath: string;
  sizeBytes: number;
  lastWriteAt: string;
  publicPath: string | null;
}

export interface IconCacheStatus {
  directory: string;
  exists: boolean;
  fileCount: number;
  totalBytes: number;
  latestWriteAt: string | null;
  filesByKind: Record<string, number>;
  filesIncluded: boolean;
  files: IconCacheFile[];
  lastWarm: IconWarmSummary | null;
  warmJob: IconWarmJobStatus;
}
