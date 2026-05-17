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

export interface ScopeConfig {
  key: ScopeKey;
  label: string;
  kind: ScopeKind;
  status: ScopeStatus;
  enabledViews: MinisterViewKey[];
}

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
  { key: 'system', label: 'SYSTEM', kind: 'system', status: 'live', enabledViews: [] },
  { key: 'info', label: 'INFO', kind: 'info', status: 'reference', enabledViews: [] },
  { key: 'analytics', label: 'ANALYTICS', kind: 'analytics', status: 'live', enabledViews: [] },
  { key: 'dev_blog', label: 'DEV BLOG', kind: 'dev_blog', status: 'live', enabledViews: [] },
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
