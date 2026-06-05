export type ScopeKey =
  | 'home'
  | 'system'
  | 'info'
  | 'analytics'
  | 'dev_blog'
  | 'mayor'
  | 'food'
  | 'willie'
  | 'defense'
  | 'welfare'
  | 'medical'
  | 'research'
  | 'industry'
  | 'economy'
  | 'chief_of_staff';

export type ScopeKind = 'home' | 'system' | 'info' | 'analytics' | 'dev_blog' | 'minister';
export type ScopeStatus = 'live' | 'planned' | 'reference';
export type MinisterViewKey = 'prompt' | 'briefing' | 'solver' | 'requests' | 'rules' | 'infographics' | 'advice';
export type HomeViewKey = 'overview';
export type SystemViewKey = 'runtime' | 'connectivity' | 'storage' | 'coverage' | 'events';
export type InfoViewKey = 'overview' | 'glossary' | 'contracts' | 'data_sources' | 'algorithms';
export type AnalyticsViewKey = 'session' | 'colony' | 'advice' | 'sse' | 'candidates';
export type DevBlogViewKey = 'features' | 'churn' | 'commits' | 'topics' | 'suggestions';
export type DashboardViewKey =
  | HomeViewKey
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
  displayLabel: string;
  emoji?: string;
  kind: ScopeKind;
  status: ScopeStatus;
  enabledViews: DashboardViewKey[];
  canRunRules?: boolean;
  canRunLlm?: boolean;
}

export const homeViews: DashboardViewDefinition[] = [
  { key: 'overview', label: 'Overview' },
];

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
  { key: 'algorithms', label: 'Algorithms' },
];

export const analyticsViews: DashboardViewDefinition[] = [
  { key: 'session', label: 'Session' },
  { key: 'colony', label: 'Colony' },
  { key: 'advice', label: 'Advice' },
  { key: 'sse', label: 'SSE' },
  { key: 'candidates', label: 'Candidates' },
];

export const devBlogViews: DashboardViewDefinition[] = [
  { key: 'features', label: 'Features' },
  { key: 'churn', label: 'Churn' },
  { key: 'commits', label: 'Commits' },
  { key: 'topics', label: 'Topics' },
  { key: 'suggestions', label: 'Suggestions' },
];

export const ministerViews: Array<{ key: MinisterViewKey; label: string }> = [
  { key: 'prompt', label: 'LLM' },
  { key: 'briefing', label: 'Briefing' },
  { key: 'solver', label: 'Solver' },
  { key: 'requests', label: 'Requests' },
  { key: 'rules', label: 'Rules' },
  { key: 'infographics', label: 'Infographics' },
  { key: 'advice', label: 'Advice' },
];

const allMinisterViews = ministerViews
  .map(view => view.key)
  .filter(view => view !== 'solver' && view !== 'requests');
const rulesOnlyMinisterViews: DashboardViewKey[] = ['briefing', 'solver', 'requests', 'rules', 'advice'];

export const scopeConfigs: ScopeConfig[] = [
  { key: 'home', label: 'CABINET', displayLabel: 'CABINET', kind: 'home', status: 'live', enabledViews: homeViews.map(view => view.key) },
  { key: 'system', label: 'SYSTEM', displayLabel: 'SYSTEM', kind: 'system', status: 'live', enabledViews: systemViews.map(view => view.key) },
  { key: 'info', label: 'INFO', displayLabel: 'INFO', kind: 'info', status: 'reference', enabledViews: infoViews.map(view => view.key) },
  { key: 'analytics', label: 'ANALYTICS', displayLabel: 'ANALYTICS', kind: 'analytics', status: 'live', enabledViews: analyticsViews.map(view => view.key) },
  { key: 'dev_blog', label: 'DEV BLOG', displayLabel: 'DEV BLOG', kind: 'dev_blog', status: 'live', enabledViews: devBlogViews.map(view => view.key) },
  { key: 'mayor', label: 'Mayor', displayLabel: '🏛️ Mayor', emoji: '🏛️', kind: 'minister', status: 'live', enabledViews: allMinisterViews, canRunLlm: true },
  { key: 'food', label: 'Chef', displayLabel: '🍲 Chef', emoji: '🍲', kind: 'minister', status: 'live', enabledViews: allMinisterViews, canRunRules: true, canRunLlm: true },
  { key: 'willie', label: 'Willie', displayLabel: '🧱 Willie', emoji: '🧱', kind: 'minister', status: 'live', enabledViews: rulesOnlyMinisterViews, canRunRules: true },
  { key: 'defense', label: 'Defense', displayLabel: '🛡️ Defense', emoji: '🛡️', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'welfare', label: 'Welfare', displayLabel: '🙂 Welfare', emoji: '🙂', kind: 'minister', status: 'live', enabledViews: allMinisterViews, canRunRules: true, canRunLlm: true },
  { key: 'medical', label: 'Medical', displayLabel: '🩺 Medical', emoji: '🩺', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'research', label: 'Research', displayLabel: '🔬 Research', emoji: '🔬', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'industry', label: 'Industry', displayLabel: '⚙️ Industry', emoji: '⚙️', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'economy', label: 'Economy', displayLabel: '🪙 Economy', emoji: '🪙', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'chief_of_staff', label: 'Chief of Staff', displayLabel: '🧭 Chief of Staff', emoji: '🧭', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
];

export function findScope(key: ScopeKey): ScopeConfig {
  return scopeConfigs.find(scope => scope.key === key) ?? scopeConfigs[0];
}

export function viewsForScope(scope: ScopeConfig): DashboardViewDefinition[] {
  const registeredViews = viewsForScopeKind(scope);
  return registeredViews.filter(view => scope.enabledViews.includes(view.key));
}

function viewsForScopeKind(scope: ScopeConfig): DashboardViewDefinition[] {
  if (scope.kind === 'home') return homeViews;
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
  ...homeViews,
  ...systemViews,
  ...infoViews,
  ...analyticsViews,
  ...devBlogViews,
  ...ministerViews,
];

export function displayMinisterName(value: string | null | undefined): string {
  if (!value) return 'unknown source';

  const normalized = normalizeMinisterReference(value);
  const scope = scopeConfigs.find(candidate =>
    candidate.kind === 'minister' &&
    ministerAliases(candidate).some(alias => normalizeMinisterReference(alias) === normalized)
  );

  return scope?.displayLabel ?? value;
}

function ministerAliases(scope: ScopeConfig): string[] {
  if (scope.key === 'food') return [scope.key, scope.label, 'Food', 'Chef', 'chef'];
  if (scope.key === 'willie') return [scope.key, scope.label, 'Construction', 'construction'];
  if (scope.key === 'welfare') return [scope.key, scope.label, 'Minister of Welfare'];
  if (scope.key === 'chief_of_staff') return [scope.key, scope.label, 'CoS', 'Chief'];
  return [scope.key, scope.label];
}

function normalizeMinisterReference(value: string): string {
  return value.trim().replace(/[\s-]+/g, '_').toLocaleLowerCase();
}
