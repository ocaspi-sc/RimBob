import type { IconRef } from '../types/icons';

export type ScopeKey =
  | 'system'
  | 'info'
  | 'analytics'
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

export type ScopeKind = 'system' | 'info' | 'analytics' | 'minister';
export type ScopeStatus = 'live' | 'planned' | 'reference';
export type MinisterViewKey = 'prompt' | 'raw_llm' | 'briefing' | 'rag' | 'rules' | 'advice';

export interface ScopeConfig {
  key: ScopeKey;
  label: string;
  emoji: string;
  icon?: IconRef;
  kind: ScopeKind;
  status: ScopeStatus;
  enabledViews: MinisterViewKey[];
}

export const ministerViews: Array<{ key: MinisterViewKey; label: string; emoji: string }> = [
  { key: 'prompt', label: 'System Prompt', emoji: '🧠' },
  { key: 'briefing', label: 'Briefing', emoji: '📋' },
  { key: 'rag', label: 'RAG', emoji: '📚' },
  { key: 'rules', label: 'Rules', emoji: '⚖️' },
  { key: 'raw_llm', label: 'Raw LLM Output', emoji: '🧾' },
  { key: 'advice', label: 'Advice', emoji: '💡' },
];

const allMinisterViews = ministerViews.map(view => view.key);

export const scopeConfigs: ScopeConfig[] = [
  { key: 'system', label: 'SYSTEM', emoji: '⚙️', icon: { kind: 'item', id: 'ComponentIndustrial' }, kind: 'system', status: 'live', enabledViews: [] },
  { key: 'info', label: 'INFO', emoji: 'ℹ️', icon: { kind: 'item', id: 'TextBook' }, kind: 'info', status: 'reference', enabledViews: [] },
  { key: 'analytics', label: 'ANALYTICS', emoji: '📈', icon: { kind: 'item', id: 'SimpleResearchBench' }, kind: 'analytics', status: 'live', enabledViews: [] },
  { key: 'mayor', label: 'Mayor', emoji: '🏛️', icon: { kind: 'item', id: 'CommsConsole' }, kind: 'minister', status: 'live', enabledViews: allMinisterViews },
  { key: 'food', label: 'Food', emoji: '🍲', icon: { kind: 'item', id: 'MealSimple' }, kind: 'minister', status: 'live', enabledViews: allMinisterViews },
  { key: 'construction', label: 'Construction', emoji: '🏗️', icon: { kind: 'item', id: 'Wall' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'defense', label: 'Defense', emoji: '🛡️', icon: { kind: 'item', id: 'Gun_Revolver' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'welfare', label: 'Welfare', emoji: '🙂', icon: { kind: 'item', id: 'Bed' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'medical', label: 'Medical', emoji: '🏥', icon: { kind: 'item', id: 'MedicineIndustrial' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'research', label: 'Research', emoji: '🔬', icon: { kind: 'item', id: 'SimpleResearchBench' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'industry', label: 'Industry', emoji: '🏭', icon: { kind: 'item', id: 'Steel' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'economy', label: 'Economy', emoji: '💰', icon: { kind: 'item', id: 'Silver' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
  { key: 'chief_of_staff', label: 'Chief of Staff', emoji: '🧭', icon: { kind: 'item', id: 'OrbitalTradeBeacon' }, kind: 'minister', status: 'planned', enabledViews: allMinisterViews },
];

export function findScope(key: ScopeKey): ScopeConfig {
  return scopeConfigs.find(scope => scope.key === key) ?? scopeConfigs[0];
}
