import { fetchTrace } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForSection, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { AdviceItem } from '../../types/advice';
import type { DashboardEvent, RuleTraceDetails } from '../../types/system';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { DynamicTable, InspectorSurface, type InspectorSurfaceConfig } from '../shared/Inspector';
import { SemanticLabel } from '../shared/SemanticIcon';
import { Timeline } from '../shared/Timeline';

const traceInspectorConfig: InspectorSurfaceConfig = {
  summaryKeys: [
    'trigger',
    'status',
    'path',
    'completedAt',
    'completed_at',
    'ruleFired',
    'rule_fired',
    'ruleDiagnostics.selectedRule',
    'escalationReason',
    'escalation_reason',
  ],
  hiddenKeys: ['ruleDiagnostics'],
  defaultOpenKeys: ['flag', 'flags', 'advice', 'emittedAdvice', 'emitted_advice'],
  preferredTables: [
    {
      key: 'advice',
      preferredColumns: ['id', 'advice_type', 'priority', 'severity', 'title', 'issued_at'],
    },
    {
      key: 'emittedAdvice',
      title: 'emittedAdvice',
      preferredColumns: ['id', 'advice_type', 'priority', 'severity', 'title', 'issued_at'],
    },
    {
      key: 'emitted_advice',
      title: 'emitted_advice',
      preferredColumns: ['id', 'advice_type', 'priority', 'severity', 'title', 'issued_at'],
    },
    {
      key: 'flags',
      preferredColumns: ['id', 'source', 'severity', 'kind', 'summary', 'created_at'],
    },
  ],
};

const activeAdviceColumns = [
  'advice_type',
  'priority',
  'severity',
  'title',
  'id',
  'issued_at',
  'expires_at',
];

export function MinisterRulesView({
  advice,
  events,
  scope,
}: {
  advice: AdviceItem[];
  events: DashboardEvent[];
  scope: ScopeConfig;
}) {
  const trace = useAsyncResource(signal => fetchTrace(scope.key, signal), [scope.key]);
  const ministerAdvice = advice.filter(item => sameMinister(item.minister, scope.label));
  const ministerEvents = events.filter(event => sameMinister(event.source, scope.label));

  if (trace.loading) {
    return <EmptyState code="TRACE">Loading latest minister trace.</EmptyState>;
  }

  return (
    <div className="minister-view rules-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2><SemanticLabel icon={iconForView('rules')}><span>Rules</span></SemanticLabel></h2>
        <p>Wake triggers, rule/LLM path, flags, and recent event context.</p>
      </header>

      {trace.error || !trace.data ? (
        <EmptyState code="TRACE NOT EXPOSED">{trace.error ?? 'No trace returned.'}</EmptyState>
      ) : (
        <>
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
          emptyMessage={`${scope.label} has no active advice in the local SSE buffer.`}
        />
      </DisclosureSection>
    </div>
  );
}

function RuleDiagnosticsPanel({ details }: { details: RuleTraceDetails }) {
  return (
    <DisclosureSection
      title={<SemanticLabel icon={iconForView('rules')}><span>Rule diagnostics</span></SemanticLabel>}
      defaultOpen
      meta={`${details.matchedSignals.length} matched / ${details.suppressedCandidates.length} suppressed`}
    >
      <div className="inspector-field-grid">
        <div className="inspector-field">
          <SemanticLabel className="inspector-field-name" icon={iconForView('rules')}><code>selectedRule</code></SemanticLabel>
          <span>{details.selectedRule ?? 'none'}</span>
        </div>
      </div>
      <DynamicTable
        rows={details.matchedSignals}
        preferredColumns={['rule', 'outcome', 'reason']}
        emptyMessage="No rules matched."
      />
      {details.suppressedCandidates.length > 0 && (
        <DisclosureSection
          title={<SemanticLabel icon={iconForView('rules')}><span>Suppressed candidates</span></SemanticLabel>}
          meta={`${details.suppressedCandidates.length} lower-priority matches`}
        >
          <DynamicTable
            rows={details.suppressedCandidates}
            preferredColumns={['rule', 'outcome', 'reason']}
          />
        </DisclosureSection>
      )}
    </DisclosureSection>
  );
}

function sameMinister(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}
