import type { MinisterViewKey, ScopeKey } from './scopes';

export interface PanelConfig {
  id: string;
  title: string;
  scope: 'system' | 'info' | 'analytics' | 'dev_blog' | 'minister';
  view?: MinisterViewKey;
  requiredCapability: string;
}

export const systemPanelRegistry: PanelConfig[] = [
  { id: 'runtime', title: 'Runtime', scope: 'system', requiredCapability: '/api/system/health' },
  { id: 'sse', title: 'Connection and Events', scope: 'system', requiredCapability: '/api/advice/stream diagnostics' },
  { id: 'tests', title: 'Test Inventory', scope: 'system', requiredCapability: '/api/system/health.tests' },
  { id: 'coverage', title: 'Endpoint Coverage', scope: 'system', requiredCapability: '/api/system/health.endpoint_coverage' },
  { id: 'rimapi-coverage', title: 'RIMAPI Coverage', scope: 'system', requiredCapability: '/api/system/health.rimapi_coverage' },
  { id: 'timeline', title: 'Recent Events', scope: 'system', requiredCapability: 'local dashboard event buffer' },
];

export const infoPanelRegistry: PanelConfig[] = [
  { id: 'info-glossary', title: 'Important Buzzwords', scope: 'info', requiredCapability: 'static dashboard reference copy' },
  { id: 'info-scope-guide', title: 'Where To Look', scope: 'info', requiredCapability: 'static dashboard reference copy' },
  { id: 'info-algorithms', title: 'Algorithms', scope: 'info', requiredCapability: 'static dashboard reference copy' },
];

export const analyticsPanelRegistry: PanelConfig[] = [
  { id: 'analytics-session', title: 'Session Analytics', scope: 'analytics', requiredCapability: '/api/system/health plus local SSE state' },
  { id: 'analytics-sse', title: 'SSE Health Summary', scope: 'analytics', requiredCapability: '/api/advice/stream diagnostics plus /api/system/health.sse' },
  { id: 'analytics-colony', title: 'Colony Analytics', scope: 'analytics', requiredCapability: '/api/colony/snapshot' },
  { id: 'analytics-advice', title: 'Advice Analytics', scope: 'analytics', requiredCapability: 'active advice feed state' },
  { id: 'analytics-candidates', title: 'Analytics Candidates', scope: 'analytics', requiredCapability: 'static dashboard reference copy' },
  { id: 'dev-blog-features', title: 'Feature Index and Tag Timeline', scope: 'dev_blog', requiredCapability: '/api/dev-blog/history' },
  { id: 'dev-blog-loc-growth', title: 'LOC Growth', scope: 'dev_blog', requiredCapability: '/api/dev-blog/history' },
  { id: 'dev-blog-commit-size', title: 'Commit Size Histogram', scope: 'dev_blog', requiredCapability: '/api/dev-blog/history' },
  { id: 'dev-blog-suggestions', title: 'Editorial Suggestions', scope: 'dev_blog', requiredCapability: '/api/dev-blog/history' },
];

export const ministerPanelRegistry: PanelConfig[] = [
  { id: 'minister-prompt', title: 'LLM', scope: 'minister', view: 'prompt', requiredCapability: 'minister prompt endpoint plus RAG health, agenda citations, and raw LLM output endpoint' },
  { id: 'minister-briefing', title: 'Briefing', scope: 'minister', view: 'briefing', requiredCapability: 'minister briefing endpoint' },
  { id: 'minister-rules', title: 'Rules', scope: 'minister', view: 'rules', requiredCapability: 'minister trace endpoint' },
  { id: 'minister-infographics', title: 'Infographics', scope: 'minister', view: 'infographics', requiredCapability: 'minister chain snapshot data' },
  { id: 'minister-advice', title: 'Advice', scope: 'minister', view: 'advice', requiredCapability: 'agenda/advice SSE state' },
];

export function panelIdFor(scope: ScopeKey, view?: MinisterViewKey): string {
  return view ? `${scope}.${view}` : scope;
}
