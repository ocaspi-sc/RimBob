import { fetchTrace } from '../../api/client';
import type { ScopeConfig } from '../../dashboard/scopes';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { AdviceItem } from '../../types/advice';
import type { DashboardEvent } from '../../types/system';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree } from '../shared/JsonTree';
import { MetricCard } from '../shared/MetricCard';
import { Timeline } from '../shared/Timeline';

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
        <h2>Rules</h2>
        <p>Wake triggers, rule/LLM path, flags, and recent event context.</p>
      </header>

      {trace.error || !trace.data ? (
        <EmptyState code="TRACE NOT EXPOSED">{trace.error ?? 'No trace returned.'}</EmptyState>
      ) : (
        <>
          <div className="metric-grid">
            <MetricCard label="Trigger" value={trace.data.trigger} />
            <MetricCard label="Status" value={trace.data.status} />
            <MetricCard label="Path" value={trace.data.path} tone={trace.data.path === 'not_exposed_yet' ? 'warn' : 'neutral'} />
            <MetricCard label="Completed" value={formatDate(trace.data.completedAt)} />
          </div>
          <DisclosureSection title="Trace detail" defaultOpen meta={trace.data.note}>
            <JsonTree value={trace.data} />
          </DisclosureSection>
        </>
      )}

      <DisclosureSection title="Recent scope events" defaultOpen meta={`${ministerEvents.length} local events`}>
        <Timeline events={ministerEvents} limit={12} />
      </DisclosureSection>

      <DisclosureSection title="Active advice emitted" meta={`${ministerAdvice.length} active`}>
        <div className="dense-table">
          <div className="dense-row header">
            <span>Type</span>
            <span>Severity</span>
            <span>Priority</span>
            <span>Title</span>
          </div>
          {ministerAdvice.map(item => (
            <div className="dense-row" key={item.id}>
              <span>{item.advice_type}</span>
              <span>{item.severity}</span>
              <span>{item.priority_score}</span>
              <span>{item.title}</span>
            </div>
          ))}
        </div>
      </DisclosureSection>
    </div>
  );
}

function sameMinister(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}

function formatDate(iso: string | null): string {
  if (!iso) return 'not yet';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleTimeString();
}
