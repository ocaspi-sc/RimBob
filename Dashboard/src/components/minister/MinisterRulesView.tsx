import { fetchTrace } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { isScopeMinister } from '../../dashboard/selectors';
import { iconForSection, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { AdviceItem } from '../../types/advice';
import type { DashboardEvent, MinisterTrace, RuleTraceDetails } from '../../types/system';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { DynamicTable, InspectorSurface, UnknownValue, type InspectorSurfaceConfig } from '../shared/Inspector';
import { SemanticLabel } from '../shared/SemanticIcon';
import { Timeline } from '../shared/Timeline';
import { MinisterEscalationCallout } from './MinisterEscalationCallout';
import { RuleCard } from './RuleCard';

const traceInspectorConfig: InspectorSurfaceConfig = {
  hiddenKeys: [
    'minister',
    'trigger',
    'status',
    'path',
    'startedAt',
    'completedAt',
    'ruleFired',
    'escalationReason',
    'errorType',
    'errorMessage',
    'adviceCount',
    'flagCount',
    'wakeupPayload',
    'note',
    'flag',
    'ruleDiagnostics',
  ],
  defaultOpenKeys: ['flag', 'flags', 'advice'],
  preferredTables: [
    {
      key: 'advice',
      preferredColumns: ['id', 'priority', 'title', 'stamp'],
    },
    {
      key: 'flags',
      preferredColumns: ['id', 'source', 'priority', 'kind', 'summary', 'created_at'],
    },
  ],
};

const activeAdviceColumns = [
  'priority',
  'title',
  'id',
  'stamp',
];

export function MinisterRulesView({
  advice,
  events,
  manualTriggerTarget,
  scope,
}: {
  advice: AdviceItem[];
  events: DashboardEvent[];
  manualTriggerTarget: string | null;
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
        <p>Wake triggers, rule/LLM path, flags, and recent event context.</p>
      </header>

      {trace.error || !trace.data ? (
        <EmptyState code="TRACE NOT EXPOSED">{trace.error ?? 'No trace returned.'}</EmptyState>
      ) : (
        <>
          <MinisterEscalationCallout trace={trace.data} />
          <TraceSummaryPanel trace={trace.data} />
          {trace.data.ruleDiagnostics && (
            <RuleDiagnosticsPanel details={trace.data.ruleDiagnostics} />
          )}
          <InspectorSurface value={trace.data} config={traceInspectorConfig} />
        </>
      )}

      <DisclosureSection
        title={<SemanticLabel icon={iconForSection('recent_scope_events')}><span>Recent scope events</span></SemanticLabel>}
        defaultOpen
        meta={`${ministerEvents.length} local events`}
      >
        <Timeline events={ministerEvents} limit={12} />
      </DisclosureSection>

      <DisclosureSection
        title={<SemanticLabel icon={iconForSection('active_advice_emitted')}><span>Active advice emitted</span></SemanticLabel>}
        meta={`${ministerAdvice.length} active`}
      >
        <DynamicTable
          rows={ministerAdvice}
          preferredColumns={activeAdviceColumns}
          emptyMessage={`${scope.displayLabel} has no active advice in the local SSE buffer.`}
        />
      </DisclosureSection>
    </div>
  );
}

function TraceSummaryPanel({ trace }: { trace: MinisterTrace }) {
  const candidateFields: Array<[string, unknown]> = [
    ['minister', trace.minister],
    ['trigger', trace.trigger],
    ['status', trace.status],
    ['path', trace.path],
    ['ruleFired', trace.ruleFired],
    ['escalationReason', trace.escalationReason],
    ['errorType', trace.errorType],
    ['errorMessage', trace.errorMessage],
    ['adviceCount', trace.adviceCount],
    ['flagCount', trace.flagCount],
    ['startedAt', trace.startedAt],
    ['completedAt', trace.completedAt],
    ['note', trace.note],
  ];
  const fields = candidateFields.filter(([, value]) => value !== null && value !== '');

  return (
    <DisclosureSection
      title={<SemanticLabel icon={iconForSection('trigger')}><span>Trigger summary</span></SemanticLabel>}
      defaultOpen
      meta={`${trace.status} / ${formatTracePath(trace.path)}`}
    >
      <div className="inspector-field-grid">
        {fields.map(([key, value]) => (
          <div className="inspector-field" key={key}>
            <SemanticLabel className="inspector-field-name" icon={iconForSection(key)}><code>{key}</code></SemanticLabel>
            <UnknownValue value={value} fieldKey={key} />
          </div>
        ))}
      </div>
    </DisclosureSection>
  );
}

function RuleDiagnosticsPanel({ details }: { details: RuleTraceDetails }) {
  const allRules = details.allRules;
  const selectedCount = allRules.filter(row => row.outcome.trim() === 'selected').length;
  const ruleMeta = `${selectedCount} selected / ${allRules.length} rules`;
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

  return (
    <DisclosureSection
      title={<SemanticLabel icon={iconForView('rules')}><span>Rule diagnostics</span></SemanticLabel>}
      defaultOpen
      meta={ruleMeta}
    >
      <div className="inspector-field-grid">
        <div className="inspector-field">
          <SemanticLabel className="inspector-field-name" icon={iconForView('rules')}><code>selectedRule</code></SemanticLabel>
          <span>{details.selectedRule ?? 'none'}</span>
        </div>
      </div>
      <DisclosureSection
        title={<SemanticLabel icon={iconForView('rules')}><span>All rules</span></SemanticLabel>}
        defaultOpen
        meta={ruleMeta}
      >
        {sortedRules.length > 0 ? (
          <div className="rule-card-list">
            {sortedRules.map(row => (
              <RuleCard
                key={row.rule}
                rule={row}
                selected={row.rule === details.selectedRule}
              />
            ))}
          </div>
        ) : (
          <EmptyState code="NO RULE CATALOG">No rule catalog was emitted by this run.</EmptyState>
        )}
      </DisclosureSection>
    </DisclosureSection>
  );
}

function formatTracePath(path: string): string {
  return path.replace(/_/g, ' ');
}

const ruleOutcomeOrder = ['selected', 'escalated', 'not_matched'];
