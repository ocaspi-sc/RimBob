export type ScopeKey =
  | 'system'
  | 'info'
  | 'analytics'
  | 'dev_blog'
  | 'mayor'
  | 'food'
  | 'construction'
  | 'defense'
  | 'welfare'
  | 'medical'
  | 'research'
  | 'industry'
  | 'economy'
  | 'chief_of_staff';

export type ScopeKind = 'system' | 'info' | 'analytics' | 'dev_blog' | 'minister';
export type ScopeStatus = 'live' | 'planned' | 'reference';
export type MinisterViewKey = 'prompt' | 'raw_llm' | 'briefing' | 'rag' | 'rules' | 'advice';
export type SystemViewKey = 'runtime' | 'connectivity' | 'storage' | 'coverage' | 'events';
export type InfoViewKey = 'overview' | 'glossary' | 'contracts' | 'data_sources';
export type AnalyticsViewKey = 'session' | 'colony' | 'advice' | 'sse' | 'candidates';
export type DevBlogViewKey = 'timeline' | 'churn' | 'commits' | 'topics' | 'suggestions';
export type DashboardViewKey =
  | MinisterViewKey
  | SystemViewKey
  | InfoViewKey
  | AnalyticsViewKey
  | DevBlogViewKey;

export interface DashboardViewDefinition {
  key: DashboardViewKey;
  label: string;
}

export interface ScopeConfig {
  key: ScopeKey;
  label: string;
  kind: ScopeKind;
  status: ScopeStatus;
  enabledViews: DashboardViewKey[];
}

export const systemViews: DashboardViewDefinition[] = [
  { key: 'runtime', label: 'Runtime' },
  { key: 'connectivity', label: 'Connectivity' },
  { key: 'storage', label: 'Storage' },
  { key: 'coverage', label: 'Coverage' },
  { key: 'events', label: 'Events' },
];

export const infoViews: DashboardViewDefinition[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'glossary', label: 'Glossary' },
  { key: 'contracts', label: 'Contracts' },
  { key: 'data_sources', label: 'Data Sources' },
];

export const analyticsViews: DashboardViewDefinition[] = [
  { key: 'session', label: 'Session' },
  { key: 'colony', label: 'Colony' },
  { key: 'advice', label: 'Advice' },
  { key: 'sse', label: 'SSE' },
  { key: 'candidates', label: 'Candidates' },
];

export const devBlogViews: DashboardViewDefinition[] = [
  { key: 'timeline', label: 'Timeline' },
  { key: 'churn', label: 'Churn' },
  { key: 'commits', label: 'Commits' },
  { key: 'topics', label: 'Topics' },
  { key: 'suggestions', label: 'Suggestions' },
];

export const ministerViews: Array<{ key: MinisterViewKey; label: string }> = [
  { key: 'prompt', label: 'System Prompt' },
  { key: 'briefing', label: 'Briefing' },
  { key: 'rag', label: 'RAG' },
  { key: 'rules', label: 'Rules' },
  { key: 'raw_llm', label: 'Raw LLM Output' },
  { key: 'advice', label: 'Advice' },
];

const allMinisterViews = ministerViews.map(view => view.key);

export const scopeConfigs: ScopeConfig[] = [
  { key: 'system', label: 'SYSTEM', kind: 'system', status: 'live', enabledViews: systemViews.map(view => view.key) },
  { key: 'info', label: 'INFO', kind: 'info', status: 'reference', enabledViews: infoViews.map(view => view.key) },
  { key: 'analytics', label: 'ANALYTICS', kind: 'analytics', status: 'live', enabledViews: analyticsViews.map(view => view.key) },
  { key: 'dev_blog', label: 'DEV BLOG', kind: 'dev_blog', status: 'live', enabledViews: devBlogViews.map(view => view.key) },
  { key: 'mayor', label: 'Mayor', kind: 'minister', status: 'live', enabledViews: allMinisterViews },
  { key: 'food', label: 'Food', kind: 'minister', status: 'live', enabledViews: allMinisterViews },
  { key: 'construction', label: 'Construction', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'defense', label: 'Defense', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'welfare', label: 'Welfare', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'medical', label: 'Medical', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'research', label: 'Research', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'industry', label: 'Industry', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'economy', label: 'Economy', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'chief_of_staff', label: 'Chief of Staff', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
];

export function findScope(key: ScopeKey): ScopeConfig {
  return scopeConfigs.find(scope => scope.key === key) ?? scopeConfigs[0];
}

export function viewsForScope(scope: ScopeConfig): DashboardViewDefinition[] {
  if (scope.kind === 'system') return systemViews;
  if (scope.kind === 'info') return infoViews;
  if (scope.kind === 'analytics') return analyticsViews;
  if (scope.kind === 'dev_blog') return devBlogViews;
  return ministerViews;
}

export function defaultViewForScope(scope: ScopeConfig): DashboardViewKey {
  if (scope.kind === 'minister') return 'advice';
  return viewsForScope(scope)[0]?.key ?? 'advice';
}

export function viewForScope(scope: ScopeConfig, view: DashboardViewKey): DashboardViewKey {
  return scope.enabledViews.includes(view) ? view : defaultViewForScope(scope);
}

export function isDashboardViewKey(value: string | null): value is DashboardViewKey {
  return typeof value === 'string' && allDashboardViews.some(view => view.key === value);
}

export function isMinisterViewKey(value: DashboardViewKey): value is MinisterViewKey {
  return ministerViews.some(view => view.key === value);
}

const allDashboardViews = [
  ...systemViews,
  ...infoViews,
  ...analyticsViews,
  ...devBlogViews,
  ...ministerViews,
];
