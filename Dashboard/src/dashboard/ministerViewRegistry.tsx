import type { ReactElement } from 'react';
import { MinisterAdviceView } from '../components/minister/MinisterAdviceView';
import { MinisterBuildQueueView } from '../components/minister/MinisterBuildQueueView';
import { MinisterBriefingView } from '../components/minister/MinisterBriefingView';
import { MinisterInfographicsView } from '../components/minister/MinisterInfographicsView';
import { MinisterPromptView } from '../components/minister/MinisterPromptView';
import { MinisterRagView } from '../components/minister/MinisterRagView';
import { MinisterRawLlmView } from '../components/minister/MinisterRawLlmView';
import { MinisterRulesView } from '../components/minister/MinisterRulesView';
import { MinisterSolverView } from '../components/minister/MinisterSolverView';
import { ministerViews, type MinisterViewKey, type ScopeConfig } from './scopes';
import { valueForScope } from './selectors';
import type { AdviceChainModel, AdviceItem, AgentFlag } from '../types/advice';
import type { MayorAgenda } from '../types/agenda';
import type { DashboardEvent, SystemHealth } from '../types/system';

export interface MinisterViewContext {
  activeAdvice: AdviceItem[];
  agenda: MayorAgenda | null;
  chains: Record<string, AdviceChainModel>;
  events: DashboardEvent[];
  flags: Record<string, AgentFlag[]>;
  previousAgenda: MayorAgenda | null;
  scope: ScopeConfig;
  stateSummaries: Record<string, string>;
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
  build_queue: ({ activeAdvice, flags, scope, systemHealth }) => (
    <MinisterBuildQueueView
      scope={scope}
      advice={activeAdvice}
      currentGameTick={systemHealth?.colony_snapshot.game_tick ?? null}
      flags={flags}
    />
  ),
  solver: ({ scope, systemHealth }) => <MinisterSolverView scope={scope} systemHealth={systemHealth} />,
  raw_llm: ({ scope, systemHealth }) => <MinisterRawLlmView scope={scope} systemHealth={systemHealth} />,
  rag: ({ agenda, scope, systemHealth }) => (
    <MinisterRagView scope={scope} agenda={agenda} systemHealth={systemHealth} />
  ),
  rules: ({ activeAdvice, events, scope }) => (
    <MinisterRulesView scope={scope} events={events} advice={activeAdvice} />
  ),
  infographics: ({ chains, scope }) => (
    <MinisterInfographicsView
      chain={valueForScope(chains, scope) ?? null}
      scope={scope}
    />
  ),
  advice: ({ activeAdvice, agenda, flags, previousAgenda, scope, stateSummaries, systemHealth }) => (
    <MinisterAdviceView
      scope={scope}
      agenda={agenda}
      previousAgenda={previousAgenda}
      advice={activeAdvice}
      currentGameTick={systemHealth?.colony_snapshot.game_tick ?? null}
      flags={valueForScope(flags, scope) ?? []}
      stateSummary={valueForScope(stateSummaries, scope) ?? null}
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
