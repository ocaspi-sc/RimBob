export interface DevBlogCommitSummary {
  hash: string;
  shortHash: string;
  at: string;
  author: string;
  subject: string;
  additions: number;
  deletions: number;
  filesChanged: number;
  tags: string[];
}

export interface DevBlogTimelinePoint {
  date: string;
  counts: Record<string, number>;
}

export interface DevBlogLocPoint {
  date: string;
  total: number;
  net: number;
  additions: number;
  deletions: number;
  commitCount: number;
}

export interface DevBlogHistogramBin {
  label: string;
  min: number;
  max: number | null;
  count: number;
}

export interface DevBlogAreaSummary {
  area: string;
  commitCount: number;
  fileCount: number;
  additions: number;
  deletions: number;
  churn: number;
}

export interface DevBlogSlice {
  label: string;
  count: number;
  churn: number;
}

export interface DevBlogDailyVelocityPoint {
  date: string;
  uniqueScopePoints: number;
  laneScopePoints: Record<string, number>;
  laneCommitCounts: Record<string, number>;
}

export interface DevBlogDailyAreaRow {
  date: string;
  uniqueScopePoints: number;
  lanes: DevBlogDailyAreaLane[];
}

export interface DevBlogDailyAreaLane {
  area: string;
  scopePoints: number;
  commitCount: number;
  commits: DevBlogLaneCommit[];
}

export interface DevBlogLaneCommit {
  hash: string;
  shortHash: string;
  at: string;
  subject: string;
  summary: string;
  sizeLabel: string;
  scopePoints: number;
  filesChanged: number;
  materialAreas: string[];
  supportingAreas: string[];
}

export interface DevBlogHistory {
  generatedAt: string;
  repositoryRoot: string;
  scannedRef: string;
  source: string;
  commitCount: number;
  firstCommitAt: string | null;
  lastCommitAt: string | null;
  totalFilesChanged: number;
  totalAdditions: number;
  totalDeletions: number;
  netLoc: number;
  averageChurn: number;
  medianChurn: number;
  largestCommits: DevBlogCommitSummary[];
  tagTimeline: DevBlogTimelinePoint[];
  locGrowth: DevBlogLocPoint[];
  commitSizeHistogram: DevBlogHistogramBin[];
  areaSummaries: DevBlogAreaSummary[];
  authorSlices: DevBlogSlice[];
  tagSlices: DevBlogSlice[];
  velocityLanes: string[];
  gridLanes: string[];
  dailyVelocity: DevBlogDailyVelocityPoint[];
  dailyAreaVelocity: DevBlogDailyAreaRow[];
  suggestions: string[];
}
