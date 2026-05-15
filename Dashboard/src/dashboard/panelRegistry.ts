import type { MinisterViewKey, ScopeKey } from './scopes';

export interface PanelConfig {
  id: string;
  title: string;
  scope: 'system' | 'info' | 'analytics' | 'minister';
  view?: MinisterViewKey;
  requiredCapability: string;
}

export const systemPanelRegistry: PanelConfig[] = [
  { id: 'runtime', title: 'Runtime', scope: 'system', requiredCapability: '/api/system/health' },
  { id: 'sse', title: 'Connection and Events', scope: 'system', requiredCapability: '/api/advice/stream diagnostics' },
  { id: 'coverage', title: 'Endpoint Coverage', scope: 'system', requiredCapability: '/api/system/health.endpoint_coverage' },
  { id: 'timeline', title: 'Recent Events', scope: 'system', requiredCapability: 'local dashboard event buffer' },
];

export const infoPanelRegistry: PanelConfig[] = [
  { id: 'info-glossary', title: 'Important Buzzwords', scope: 'info', requiredCapability: 'static dashboard reference copy' },
  { id: 'info-scope-guide', title: 'Where To Look', scope: 'info', requiredCapability: 'static dashboard reference copy' },
];

export const analyticsPanelRegistry: PanelConfig[] = [
  { id: 'analytics-session', title: 'Session Analytics', scope: 'analytics', requiredCapability: '/api/system/health plus local SSE state' },
  { id: 'analytics-sse', title: 'SSE Health Summary', scope: 'analytics', requiredCapability: '/api/advice/stream diagnostics plus /api/system/health.sse' },
  { id: 'analytics-colony', title: 'Colony Analytics', scope: 'analytics', requiredCapability: '/api/colony/snapshot' },
  { id: 'analytics-advice', title: 'Advice Analytics', scope: 'analytics', requiredCapability: 'active advice feed state' },
  { id: 'analytics-candidates', title: 'Analytics Candidates', scope: 'analytics', requiredCapability: 'static dashboard reference copy' },
];

export const ministerPanelRegistry: PanelConfig[] = [
  { id: 'minister-prompt', title: 'System Prompt', scope: 'minister', view: 'prompt', requiredCapability: 'minister prompt endpoint' },
  { id: 'minister-briefing', title: 'Briefing', scope: 'minister', view: 'briefing', requiredCapability: 'minister briefing endpoint' },
  { id: 'minister-rag', title: 'RAG', scope: 'minister', view: 'rag', requiredCapability: 'minister RAG endpoint or agenda citations' },
  { id: 'minister-rules', title: 'Rules', scope: 'minister', view: 'rules', requiredCapability: 'minister trace endpoint' },
  { id: 'minister-raw-llm', title: 'Raw LLM Output', scope: 'minister', view: 'raw_llm', requiredCapability: 'minister raw LLM output endpoint' },
  { id: 'minister-advice', title: 'Advice', scope: 'minister', view: 'advice', requiredCapability: 'agenda/advice SSE state' },
];

export function panelIdFor(scope: ScopeKey, view?: MinisterViewKey): string {
  return view ? `${scope}.${view}` : scope;
}
