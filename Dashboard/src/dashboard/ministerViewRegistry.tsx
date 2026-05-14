import type { ReactElement } from 'react';
import { MinisterAdviceView } from '../components/minister/MinisterAdviceView';
import { MinisterBriefingView } from '../components/minister/MinisterBriefingView';
import { MinisterPromptView } from '../components/minister/MinisterPromptView';
import { MinisterRagView } from '../components/minister/MinisterRagView';
import { MinisterRawLlmView } from '../components/minister/MinisterRawLlmView';
import { MinisterRulesView } from '../components/minister/MinisterRulesView';
import { ministerViews, type MinisterViewKey, type ScopeConfig } from './scopes';
import type { AdviceItem } from '../types/advice';
import type { MayorAgenda } from '../types/agenda';
import type { DashboardEvent, SystemHealth } from '../types/system';

export interface MinisterViewContext {
  activeAdvice: AdviceItem[];
  agenda: MayorAgenda | null;
  events: DashboardEvent[];
  previousAgenda: MayorAgenda | null;
  scope: ScopeConfig;
  systemHealth: SystemHealth | null;
}

type MinisterViewRenderer = (context: MinisterViewContext) => ReactElement;

export interface MinisterViewDefinition {
  key: MinisterViewKey;
  label: string;
  render: MinisterViewRenderer;
}

const ministerViewRenderers: Record<MinisterViewKey, MinisterViewRenderer> = {
  prompt: ({ scope }) => <MinisterPromptView scope={scope} />,
  briefing: ({ scope }) => <MinisterBriefingView scope={scope} />,
  raw_llm: ({ scope }) => <MinisterRawLlmView scope={scope} />,
  rag: ({ agenda, scope, systemHealth }) => (
    <MinisterRagView scope={scope} agenda={agenda} systemHealth={systemHealth} />
  ),
  rules: ({ activeAdvice, events, scope }) => (
    <MinisterRulesView scope={scope} events={events} advice={activeAdvice} />
  ),
  advice: ({ activeAdvice, agenda, previousAgenda, scope }) => (
    <MinisterAdviceView
      scope={scope}
      agenda={agenda}
      previousAgenda={previousAgenda}
      advice={activeAdvice}
    />
  ),
};

export const ministerViewRegistry: MinisterViewDefinition[] = ministerViews.map(view => ({
  ...view,
  render: ministerViewRenderers[view.key],
}));

export function findMinisterView(key: MinisterViewKey): MinisterViewDefinition {
  return ministerViewRegistry.find(view => view.key === key) ?? ministerViewRegistry[ministerViewRegistry.length - 1];
}
