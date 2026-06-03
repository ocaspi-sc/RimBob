import type { AdviceItem } from '../../types/advice';
import type { MayorAgenda } from '../../types/agenda';
import type { ColonySnapshot } from '../../types/colony';
import type { DashboardEvent, StreamDiagnostics, SystemHealth } from '../../types/system';
import type { DashboardViewDefinition, DashboardViewKey } from '../../dashboard/scopes';
import type { SemanticIconSpec } from '../../dashboard/semanticIcons';
import { iconForActionKind, iconForField, iconForInfoTerm, iconForScope } from '../../dashboard/semanticIcons';
import { DisclosureSection } from '../shared/DisclosureSection';
import { MetricCard } from '../shared/MetricCard';
import { SemanticLabel } from '../shared/SemanticIcon';
import { Timeline } from '../shared/Timeline';
import { ViewTabs } from '../layout/ViewTabs';

interface AnalyticsIdea {
  name: string;
  signal: string;
  value: string;
}

interface CountRow {
  label: string;
  count: number;
}

const analyticsIdeas: AnalyticsIdea[] = [
  {
    name: 'Advice pressure',
    signal: 'Active critical/high advice by minister',
    value: 'Shows which subsystem is currently dominating the player attention budget.',
  },
  {
    name: 'Run freshness',
    signal: 'Last successful trace age per live minister',
    value: 'Highlights stale ministers before the player trusts old advice.',
  },
  {
    name: 'Escalation ratio',
    signal: 'Rules path vs. LLM escalation count over recent runs',
    value: 'Shows whether a minister is cheap by default or leaning too hard on the provider.',
  },
  {
    name: 'Telemetry coverage',
    signal: 'Endpoint coverage states plus missing briefing fields',
    value: 'Separates weak advice from missing live-state inputs.',
  },
  {
    name: 'Feedback funnel',
    signal: 'Accept / Dismiss / Pushback per minister once feedback is wired',
    value: 'Turns player corrections into a visible refinement queue.',
  },
  {
    name: 'Repeat issue heatmap',
    signal: 'Stable issue ids recurring across sessions and replay corpus records',
    value: 'Finds rules that keep emitting the same unresolved memo.',
  },
  {
    name: 'SSE event cadence',
    signal: 'Gap between agenda_update, advice_snapshot, advice, and ping events',
    value: 'Shows whether the live feed is quiet because RimBob is idle or because the stream is stale.',
  },
];

export function AnalyticsOverview({
  activeAdvice,
  agenda,
  events,
  health,
  onSelectView,
  selectedView,
  snapshot,
  stream,
  views,
}: {
  activeAdvice: AdviceItem[];
  agenda: MayorAgenda | null;
  events: DashboardEvent[];
  health: SystemHealth | null;
  onSelectView: (view: DashboardViewKey) => void;
  selectedView: DashboardViewKey;
  snapshot: ColonySnapshot | null;
  stream: StreamDiagnostics;
  views: DashboardViewDefinition[];
}) {
  const analytics = buildAnalytics(activeAdvice, agenda, health, snapshot);
  const sseAnalytics = buildSseAnalytics(stream, health);
  const priorityCounts = countBy(activeAdvice, item => item.priority);
  const ministerCounts = countBy(activeAdvice, item => item.minister);
  const actionKindCounts = countBy(
    activeAdvice.flatMap(item => item.actions),
    action => action.kind,
  );

  return (
    <div className="analytics-overview">
      <header className="analytics-hero system-card">
        <div>
          <span className="eyebrow">Derived signals</span>
          <h2><SemanticLabel icon={iconForScope('analytics')} size="sm"><span>ANALYTICS</span></SemanticLabel></h2>
          <p>Interpreted live metrics from bounded dashboard inputs. Raw diagnostics stay in SYSTEM; definitions stay in INFO.</p>
        </div>
        <div className="scope-boundary-strip analytics-boundary-strip">
          <span>Live signals</span>
          <span>Advice mix</span>
          <span>Colony pressure</span>
          <span>SSE summary</span>
        </div>
        <div className="info-hero-metrics">
          <MetricCard label={metricLabel('active_advice', 'Active advice')} value={activeAdvice.length} tone={analytics.highPressureAdvice > 0 ? 'warn' : 'neutral'} />
          <MetricCard label={metricLabel('agenda_priorities', 'Agenda priorities')} value={analytics.activeAgendaPriorities} />
          <MetricCard label={metricLabel('events', 'Events')} value={events.length} tone={stream.state === 'error' ? 'warn' : 'neutral'} />
        </div>
      </header>

      <ViewTabs
        activeView={selectedView}
        ariaLabel="ANALYTICS signal views"
        views={views}
        onSelect={onSelectView}
      />

      {selectedView === 'session' && (
      <section className="system-grid info-metric-grid">
        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Session analytics</span>
            <h2>Current signal load</h2>
          </div>
          <div className="metric-grid">
            <MetricCard label={metricLabel('priority', 'Critical/high')} value={analytics.highPressureAdvice} tone={analytics.highPressureAdvice > 0 ? 'warn' : 'ok'} />
            <MetricCard label={metricLabel('data_coverage', 'Coverage')} value={analytics.coverageLabel} tone={analytics.coverageTone} />
            <MetricCard label={metricLabel('rag', 'RAG chunks')} value={health?.rag.chunk_count ?? 'n/a'} />
            <MetricCard label={metricLabel('sse_state', 'SSE state')} value={stream.state} tone={sseAnalytics.clientTone} />
            <MetricCard label={metricLabel('latest_event', 'Latest event')} value={sseAnalytics.lastEventType} />
            <MetricCard label={metricLabel('trace_count', 'Trace count')} value={health?.traces.length ?? 'n/a'} />
          </div>
        </div>
      </section>
      )}

      {selectedView === 'colony' && (
      <section className="system-grid info-metric-grid">
        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Colony analytics</span>
            <h2>Current pressure</h2>
          </div>
          <div className="metric-grid">
            <MetricCard label={metricLabel('food_buffer', 'Food buffer')} value={analytics.foodDaysLabel} tone={analytics.foodTone} />
            <MetricCard label={metricLabel('mood', 'Mood')} value={analytics.moodLabel} tone={analytics.moodTone} />
            <MetricCard label={metricLabel('power_net', 'Power net')} value={analytics.powerNetLabel} tone={analytics.powerTone} />
            <MetricCard label={metricLabel('downed', 'Downed')} value={snapshot?.medical.downed ?? 'n/a'} tone={(snapshot?.medical.downed ?? 0) > 0 ? 'warn' : 'ok'} />
            <MetricCard label={metricLabel('threat', 'Threat')} value={snapshot?.threat.activeRaid ? 'raid' : snapshot ? 'clear' : 'n/a'} tone={snapshot?.threat.activeRaid ? 'error' : 'neutral'} />
            <MetricCard label={metricLabel('wealth_colonist', 'Wealth / colonist')} value={snapshot ? Math.round(snapshot.wealth.wealthPerColonist).toLocaleString() : 'n/a'} />
          </div>
        </div>
      </section>
      )}

      {selectedView === 'sse' && (
      <DisclosureSection title={<SemanticLabel icon={iconForField('sse')}><span>SSE health summary</span></SemanticLabel>} defaultOpen meta={sseAnalytics.summary}>
        <div className="sse-info-grid">
          <div className="system-card compact-info-card">
            <div className="section-heading">
              <span className="eyebrow">Client</span>
              <h2>Dashboard stream</h2>
            </div>
            <div className="metric-grid compact">
              <MetricCard label={metricLabel('sse_state', 'State')} value={stream.state} tone={sseAnalytics.clientTone} />
              <MetricCard label={metricLabel('events', 'Events')} value={stream.eventCount} />
              <MetricCard label={metricLabel('reconnects', 'Reconnects')} value={stream.reconnectCount} tone={stream.reconnectCount > 0 ? 'warn' : 'ok'} />
              <MetricCard label={metricLabel('last_event', 'Last event age')} value={sseAnalytics.lastEventAge} tone={sseAnalytics.lastEventTone} />
            </div>
            <div className="stacked-lines sse-detail-lines">
              <InfoLine label="Last event" value={sseAnalytics.lastEventType} />
              <InfoLine label="Last event id" value={stream.lastEventId ?? 'none'} />
              <InfoLine label="Last error" value={formatMaybeDate(stream.lastErrorAt)} />
            </div>
          </div>

          <div className="system-card compact-info-card">
            <div className="section-heading">
              <span className="eyebrow">Server</span>
              <h2>/api/advice/stream</h2>
            </div>
            <div className="metric-grid compact">
              <MetricCard label={metricLabel('connections', 'Connections')} value={health?.sse.activeConnections ?? 'n/a'} tone={(health?.sse.activeConnections ?? 0) > 0 ? 'ok' : 'neutral'} />
              <MetricCard label={metricLabel('server_events', 'Server events')} value={health?.sse.eventCount ?? 'n/a'} />
              <MetricCard label={metricLabel('error', 'Server errors')} value={health?.sse.errorCount ?? 'n/a'} tone={(health?.sse.errorCount ?? 0) > 0 ? 'warn' : 'ok'} />
              <MetricCard label={metricLabel('last_event', 'Last server event')} value={formatMaybeDate(health?.sse.lastEventAt ?? null)} />
            </div>
            <div className="stacked-lines sse-detail-lines">
              <InfoLine label="Event type" value={health?.sse.lastEventType ?? 'none'} />
              <InfoLine label="Event id" value={health?.sse.lastEventId ?? 'none'} />
              <InfoLine label="Last server error" value={health?.sse.lastError ?? 'none'} />
            </div>
          </div>
        </div>
      </DisclosureSection>
      )}

      {selectedView === 'advice' && (
      <DisclosureSection title={<SemanticLabel icon={iconForField('advice')}><span>Advice analytics</span></SemanticLabel>} defaultOpen meta={`${activeAdvice.length} active cards`}>
        <div className="analytics-columns">
          <CountPanel title="Priority mix" titleIcon={iconForField('priority')} rows={priorityCounts} empty="No active advice priorities." />
          <CountPanel title="Minister mix" titleIcon={iconForField('minister')} rows={ministerCounts} empty="No active minister advice." />
          <CountPanel title="Action mix" titleIcon={iconForField('actions')} rows={actionKindCounts} empty="No active advice actions." iconForRow={iconForActionKind} />
        </div>
      </DisclosureSection>
      )}

      {selectedView === 'candidates' && (
      <DisclosureSection title={<SemanticLabel icon={iconForScope('analytics')}><span>Analytics worth adding next</span></SemanticLabel>} defaultOpen meta={`${analyticsIdeas.length} candidates`}>
        <div className="analytics-ideas">
          {analyticsIdeas.map(idea => (
            <article key={idea.name} className="analytics-idea">
              <div>
                <SemanticLabel icon={iconForInfoTerm(idea.name, idea.signal)}><strong>{idea.name}</strong></SemanticLabel>
                <span>{idea.signal}</span>
              </div>
              <p>{idea.value}</p>
            </article>
          ))}
        </div>
      </DisclosureSection>
      )}

      {selectedView === 'session' && (
      <DisclosureSection title={<SemanticLabel icon={iconForField('events')}><span>Recent dashboard events</span></SemanticLabel>} meta={`${events.length} buffered`}>
        <Timeline events={events} limit={10} />
      </DisclosureSection>
      )}
    </div>
  );
}

function CountPanel({
  empty,
  iconForRow,
  rows,
  title,
  titleIcon,
}: {
  empty: string;
  iconForRow?: (label: string) => SemanticIconSpec | undefined;
  rows: CountRow[];
  title: string;
  titleIcon?: SemanticIconSpec;
}) {
  return (
    <section className="count-panel">
      <h3><SemanticLabel icon={titleIcon}><span>{title}</span></SemanticLabel></h3>
      {rows.length === 0 ? (
        <div className="muted-row">{empty}</div>
      ) : (
        <div className="count-rows">
          {rows.map(row => (
            <div className="count-row" key={row.label}>
              <SemanticLabel icon={iconForRow?.(row.label) ?? iconForField(row.label)}>
                <span>{formatLabel(row.label)}</span>
              </SemanticLabel>
              <strong>{row.count}</strong>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}

function metricLabel(key: string, label: string) {
  return <SemanticLabel icon={iconForField(key)}><span>{label}</span></SemanticLabel>;
}

function InfoLine({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="info-line">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function buildAnalytics(
  activeAdvice: AdviceItem[],
  agenda: MayorAgenda | null,
  health: SystemHealth | null,
  snapshot: ColonySnapshot | null,
) {
  const highPressureAdvice = activeAdvice.filter(item => item.priority === 'critical' || item.priority === 'high').length;
  const activeAgendaPriorities = agenda?.short_term.filter(item => item.status === 'active').length ?? 0;
  const coverageRows = health?.endpoint_coverage ?? [];
  const coveredRows = coverageRows.filter(row => row.state === 'available').length;
  const coverageLabel = coverageRows.length === 0
    ? 'n/a'
    : `${Math.round((coveredRows / coverageRows.length) * 100)}%`;

  const foodDays = snapshot?.food.estimatedDaysOfFood ?? null;
  const averageMood = snapshot?.mood.averageMood ?? null;
  const powerNet = snapshot?.power.netW ?? null;

  return {
    activeAgendaPriorities,
    coverageLabel,
    coverageTone: coverageRows.length === 0 ? 'neutral' as const : coveredRows < coverageRows.length ? 'warn' as const : 'ok' as const,
    foodDaysLabel: foodDays === null ? 'unknown' : `${foodDays.toFixed(1)}d`,
    foodTone: foodDays !== null && foodDays < 7 ? 'warn' as const : 'neutral' as const,
    highPressureAdvice,
    moodLabel: averageMood === null ? 'n/a' : `${Math.round(averageMood * 100)}%`,
    moodTone: snapshot && (snapshot.mood.breakRiskCount > 0 || snapshot.mood.averageMood < 0.45) ? 'warn' as const : 'neutral' as const,
    powerNetLabel: powerNet === null ? 'n/a' : `${powerNet >= 0 ? '+' : ''}${Math.round(powerNet)} W`,
    powerTone: powerNet !== null && powerNet < 0 ? 'warn' as const : 'neutral' as const,
  };
}

function countBy<T>(items: T[], keyFor: (item: T) => string | null | undefined): CountRow[] {
  const counts = new Map<string, number>();

  for (const item of items) {
    const key = keyFor(item) || 'unknown';
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }

  return Array.from(counts.entries())
    .map(([label, count]) => ({ label, count }))
    .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
}

function buildSseAnalytics(stream: StreamDiagnostics, health: SystemHealth | null) {
  const lastEventType = stream.lastEventType ?? health?.sse.lastEventType ?? 'none';
  const lastEventAt = stream.lastEventAt ?? health?.sse.lastEventAt ?? null;
  const lastEventAge = formatAge(lastEventAt);
  const hasError = stream.state === 'error' || (health?.sse.errorCount ?? 0) > 0;

  return {
    clientTone: stream.state === 'open' ? 'ok' as const : stream.state === 'error' ? 'warn' as const : 'neutral' as const,
    lastEventAge,
    lastEventTone: ageTone(lastEventAt),
    lastEventType,
    summary: hasError
      ? 'stream has warnings'
      : `${stream.eventCount} client events / ${health?.sse.eventCount ?? 'n/a'} server events`,
  };
}

function formatAge(iso: string | null): string {
  if (!iso) return 'none';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const seconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (seconds < 60) return `${seconds}s`;

  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m`;

  const hours = Math.round(minutes / 60);
  return `${hours}h`;
}

function ageTone(iso: string | null): 'neutral' | 'ok' | 'warn' {
  if (!iso) return 'neutral';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return 'neutral';

  return Date.now() - date.getTime() > 60_000 ? 'warn' : 'ok';
}

function formatMaybeDate(iso: string | null): string {
  if (!iso) return 'none';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString();
}

function formatLabel(value: string): string {
  return value
    .replace(/_/g, ' ')
    .replace(/\b\w/g, char => char.toUpperCase());
}
