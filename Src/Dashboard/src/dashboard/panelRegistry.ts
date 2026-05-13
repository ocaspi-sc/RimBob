import type { MinisterViewKey, ScopeKey } from './scopes';

export interface PanelConfig {
  id: string;
  title: string;
  scope: 'system' | 'minister';
  view?: MinisterViewKey;
  requiredCapability: string;
}

export const systemPanelRegistry: PanelConfig[] = [
  { id: 'runtime', title: 'Runtime', scope: 'system', requiredCapability: '/api/system/health' },
  { id: 'sse', title: 'Connection and Events', scope: 'system', requiredCapability: '/api/advice/stream diagnostics' },
  { id: 'coverage', title: 'Endpoint Coverage', scope: 'system', requiredCapability: '/api/system/health.endpoint_coverage' },
  { id: 'timeline', title: 'Recent Events', scope: 'system', requiredCapability: 'local dashboard event buffer' },
];

export const ministerPanelRegistry: PanelConfig[] = [
  { id: 'minister-prompt', title: 'System Prompt', scope: 'minister', view: 'prompt', requiredCapability: 'minister prompt endpoint' },
  { id: 'minister-raw-llm', title: 'Raw LLM Output', scope: 'minister', view: 'raw_llm', requiredCapability: 'minister raw LLM output endpoint' },
  { id: 'minister-briefing', title: 'Briefing', scope: 'minister', view: 'briefing', requiredCapability: 'minister briefing endpoint' },
  { id: 'minister-rag', title: 'RAG', scope: 'minister', view: 'rag', requiredCapability: 'minister RAG endpoint or agenda citations' },
  { id: 'minister-rules', title: 'Rules', scope: 'minister', view: 'rules', requiredCapability: 'minister trace endpoint' },
  { id: 'minister-advice', title: 'Advice', scope: 'minister', view: 'advice', requiredCapability: 'agenda/advice SSE state' },
];

export function panelIdFor(scope: ScopeKey, view?: MinisterViewKey): string {
  return view ? `${scope}.${view}` : scope;
}
