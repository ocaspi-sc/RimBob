export type IconKind = 'item' | 'terrain' | 'faction' | 'pawn' | string;

export interface IconRef {
  kind: IconKind;
  id: string;
}

export interface IconWarmFailure {
  kind: string;
  id: string;
  error: string;
}

export interface IconWarmSummary {
  startedAt: string;
  completedAt: string;
  totalCandidates: number;
  succeeded: number;
  failed: number;
  skipped: number;
  itemCandidates: number;
  terrainCandidates: number;
  factionCandidates: number;
  failures: IconWarmFailure[];
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
  files: IconCacheFile[];
  lastWarm: IconWarmSummary | null;
}
