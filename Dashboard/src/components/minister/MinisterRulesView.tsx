import { fetchTrace } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import type { MinisterViewKey } from '../../dashboard/scopes';
import { isScopeMinister } from '../../dashboard/selectors';
import { iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { AdviceItem } from '../../types/advice';
import type { DashboardEvent, RuleTraceDetails } from '../../types/system';
import { EmptyState } from '../shared/EmptyState';
import { SemanticLabel } from '../shared/SemanticIcon';
import { RuleCard } from './RuleCard';

export function MinisterRulesView({
  advice,
  events,
  manualTriggerTarget,
  onSelectView,
  scope,
}: {
  advice: AdviceItem[];
  events: DashboardEvent[];
  manualTriggerTarget: string | null;
  onSelectView: (view: MinisterViewKey) => void;
  scope: ScopeConfig;
}) {
  const ministerAdvice = advice.filter(item => isScopeMinister(item.minister, scope));
  const ministerEvents = events.filter(event => isScopeMinister(event.source, scope));
  const latestMinisterEventId = ministerEvents[0]?.id ?? 'none';
  const latestAdviceIssuedAt = ministerAdvice[0]?.stamp.issued_at ?? 'none';
  const trace = useAsyncResource(signal => fetchTrace(scope.key, signal), [
    scope.key,
    latestMinisterEventId,
    latestAdviceIssuedAt,
    manualTriggerTarget,
  ]);

  if (trace.loading) {
    return <EmptyState code="TRACE">Loading latest minister trace.</EmptyState>;
  }

  return (
    <div className="minister-view rules-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.displayLabel}</span>
        <h2><SemanticLabel icon={iconForView('rules')}><span>Rules</span></SemanticLabel></h2>
      </header>

      {trace.error || !trace.data ? (
        <EmptyState code="TRACE NOT EXPOSED">{trace.error ?? 'No trace returned.'}</EmptyState>
      ) : trace.data.ruleDiagnostics ? (
        <RuleDiagnosticsPanel
          advice={ministerAdvice}
          details={trace.data.ruleDiagnostics}
          onSelectView={onSelectView}
          scope={scope}
        />
      ) : (
        <EmptyState code="NO RULE DIAGNOSTICS">No rule diagnostics were emitted by this trace.</EmptyState>
      )}
    </div>
  );
}

function RuleDiagnosticsPanel({
  advice,
  details,
  onSelectView,
  scope,
}: {
  advice: AdviceItem[];
  details: RuleTraceDetails;
  onSelectView: (view: MinisterViewKey) => void;
  scope: ScopeConfig;
}) {
  const allRules = details.allRules;
  const adviceByRule = mapAdviceByRule(advice, scope);
  const openAdvice = (adviceId: string) => {
    prepareAdviceAnchor(adviceId);
    onSelectView('advice');
    window.setTimeout(() => {
      document.getElementById(adviceId)?.scrollIntoView({ block: 'start' });
    }, 75);
  };
  const outcomeRanks = new Map(ruleOutcomeOrder.map((outcome, index) => [outcome, index]));
  const sortedRules = allRules
    .map((row, index) => ({ index, row }))
    .sort((left, right) => {
      const leftOutcome = left.row.outcome.trim() || 'unknown';
      const rightOutcome = right.row.outcome.trim() || 'unknown';
      const leftRank = outcomeRanks.get(leftOutcome) ?? ruleOutcomeOrder.length;
      const rightRank = outcomeRanks.get(rightOutcome) ?? ruleOutcomeOrder.length;
      if (leftRank !== rightRank) return leftRank - rightRank;
      return left.index - right.index;
    })
    .map(item => item.row);

  if (sortedRules.length === 0) {
    return <EmptyState code="NO RULE CATALOG">No rule catalog was emitted by this run.</EmptyState>;
  }

  return (
    <div className="rule-card-list">
      {sortedRules.map(row => (
        <RuleCard
          advice={adviceByRule.get(row.rule)}
          key={row.rule}
          onOpenAdvice={openAdvice}
          rule={row}
          selected={row.rule === details.selectedRule}
        />
      ))}
    </div>
  );
}

function mapAdviceByRule(advice: AdviceItem[], scope: ScopeConfig): Map<string, AdviceItem> {
  const prefixes = ruleAdviceIdPrefixes(scope);
  const byRule = new Map<string, AdviceItem>();

  for (const item of advice) {
    for (const prefix of prefixes) {
      if (item.id.startsWith(prefix)) {
        byRule.set(item.id.slice(prefix.length), item);
        break;
      }
    }
  }

  return byRule;
}

function ruleAdviceIdPrefixes(scope: ScopeConfig): string[] {
  const normalized = [
    scope.key,
    scope.label,
    scope.displayLabel,
  ]
    .map(value => normalizeAdviceIdPrefix(value))
    .filter((value): value is string => value !== null);

  return [...new Set(normalized)].map(value => `${value}_`);
}

function normalizeAdviceIdPrefix(value: string): string | null {
  const normalized = value
    .toLocaleLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');

  return normalized.length > 0 ? normalized : null;
}

function prepareAdviceAnchor(adviceId: string) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    const url = new URL(window.location.href);
    url.hash = adviceId;
    window.history.replaceState(window.history.state, '', `${url.pathname}${url.search}${url.hash}`);
  } catch {
    // URL history can be unavailable in restricted browser contexts; selecting the Advice tab still works.
  }
}

const ruleOutcomeOrder = ['selected', 'escalated', 'not_matched'];
