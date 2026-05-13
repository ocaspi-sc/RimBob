export type ScopeKey =
  | 'system'
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

export type ScopeKind = 'system' | 'minister';
export type ScopeStatus = 'live' | 'planned';
export type MinisterViewKey = 'prompt' | 'briefing' | 'rag' | 'rules' | 'advice';

export interface ScopeConfig {
  key: ScopeKey;
  label: string;
  emoji: string;
  kind: ScopeKind;
  status: ScopeStatus;
  enabledViews: MinisterViewKey[];
}

export const ministerViews: Array<{ key: MinisterViewKey; label: string }> = [
  { key: 'prompt', label: 'System Prompt' },
  { key: 'briefing', label: 'Briefing' },
  { key: 'rag', label: 'RAG' },
  { key: 'rules', label: 'Rules' },
  { key: 'advice', label: 'Advice' },
];

const allMinisterViews = ministerViews.map(view => view.key);

export const scopeConfigs: ScopeConfig[] = [
  { key: 'system', label: 'SYSTEM', emoji: '⚙️', kind: 'system', status: 'live', enabledViews: [] },
  { key: 'mayor', label: 'Mayor', emoji: '🏛️', kind: 'minister', status: 'live', enabledViews: allMinisterViews },
  { key: 'food', label: 'Food', emoji: '🍲', kind: 'minister', status: 'live', enabledViews: allMinisterViews },
  { key: 'construction', label: 'Construction', emoji: '🏗️', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'defense', label: 'Defense', emoji: '🛡️', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'welfare', label: 'Welfare', emoji: '🙂', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'medical', label: 'Medical', emoji: '🏥', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'research', label: 'Research', emoji: '🔬', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'industry', label: 'Industry', emoji: '🏭', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'economy', label: 'Economy', emoji: '💰', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'chief_of_staff', label: 'Chief of Staff', emoji: '🧭', kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
];

export function findScope(key: ScopeKey): ScopeConfig {
  return scopeConfigs.find(scope => scope.key === key) ?? scopeConfigs[0];
}
