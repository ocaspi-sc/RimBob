import type { RimAIStatus } from '../../types/status';
import type { DashboardEvent, StreamDiagnostics, SystemHealth } from '../../types/system';
import { systemPanelRegistry } from '../../dashboard/panelRegistry';
import { CoverageTable } from '../shared/DataCoverage';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { MetricCard } from '../shared/MetricCard';
import { StatusPill } from '../shared/StatusPill';
import { Timeline } from '../shared/Timeline';

export function SystemOverview({
  events,
  health,
  healthError,
  status,
  stream,
}: {
  events: DashboardEvent[];
  health: SystemHealth | null;
  healthError: string | null;
  status: RimAIStatus | null;
  stream: StreamDiagnostics;
}) {
  const backendSse = health?.sse;
  const llmStatus = health?.llm.status ?? status?.llm_status ?? ((status?.llm_configured ?? health?.llm.configured) ? 'ready' : 'missing_key');
  const replay = health?.logs.replay_corpus;

  return (
    <div className="system-overview">
      <div className="panel-registry-note">
        <span>Panel registry</span>
        {systemPanelRegistry.map(panel => (
          <code key={panel.id}>{panel.id}</code>
        ))}
      </div>

      {healthError && (
        <EmptyState code="SYSTEM HEALTH DEGRADED">{healthError}</EmptyState>
      )}

      <section className="system-grid">
        <div className="system-card runtime-card">
          <div className="section-heading">
            <span className="eyebrow">Runtime</span>
            <h2>Host Loop</h2>
          </div>
          <div className="metric-grid">
            <MetricCard label="Host" value={status?.server ?? health?.runtime.server ?? 'checking'} tone={status ? 'ok' : 'neutral'} />
            <MetricCard label="RIMAPI" value={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'reachable' : 'waiting'} tone={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'ok' : 'warn'} />
            <MetricCard label="Agenda" value={health?.runtime.agenda_version ?? status?.agenda_version ?? 'none'} />
            <MetricCard label="Advice" value={health?.runtime.active_advice_count ?? 'n/a'} />
            <MetricCard label="Flags" value={health?.runtime.active_flag_count ?? 'n/a'} />
            <MetricCard label="Mayor" value={status?.mayor_running ? 'running' : 'idle'} tone={status?.mayor_last_error ? 'error' : status?.mayor_running ? 'ok' : 'neutral'} />
          </div>
        </div>

        <div className="system-card llm-card">
          <div className="section-heading">
            <span className="eyebrow">LLM</span>
            <h2>Gemini</h2>
          </div>
          <div className="stacked-lines">
            <StatusPill tone={llmToneFor(llmStatus)}>
              {llmStatus.replace(/_/g, ' ')}
            </StatusPill>
            <InfoLine label="Configured" value={(status?.llm_configured ?? health?.llm.configured) ? 'yes' : 'no'} />
            <InfoLine label="Last event" value={formatMaybeDate(health?.llm.last_event_at ?? status?.llm_last_event_at ?? null)} />
            <InfoLine label="Last success" value={formatMaybeDate(health?.llm.last_success_at ?? status?.mayor_last_llm_success_at ?? null)} />
            <InfoLine label="Last error" value={shorten(health?.llm.last_error ?? status?.llm_last_error ?? status?.mayor_last_error ?? 'none')} />
            <InfoLine label="Token usage" value={health?.llm.token_usage ?? 'not exposed yet'} />
          </div>
        </div>

        <div className="system-card rag-card">
          <div className="section-heading">
            <span className="eyebrow">RAG</span>
            <h2>Knowledge</h2>
          </div>
          <div className="stacked-lines">
            <InfoLine label="Enabled" value={health?.rag.enabled ? 'yes' : 'unknown'} />
            <InfoLine label="Chunks" value={health?.rag.chunk_count ?? 'n/a'} />
            <InfoLine label="Model" value={health?.rag.embedding_model ?? 'not exposed'} />
            <InfoLine label="Guides" value={health?.rag.guides_root ?? 'not exposed'} />
          </div>
        </div>

        <div className="system-card sse-card">
          <div className="section-heading">
            <span className="eyebrow">Connection</span>
            <h2>SSE Diagnostics</h2>
          </div>
          <div className="metric-grid compact">
            <MetricCard label="Client" value={stream.state} tone={stream.state === 'open' ? 'ok' : stream.state === 'error' ? 'warn' : 'neutral'} />
            <MetricCard label="Client events" value={stream.eventCount} />
            <MetricCard label="Reconnects" value={stream.reconnectCount} tone={stream.reconnectCount > 0 ? 'warn' : 'neutral'} />
            <MetricCard label="Server events" value={backendSse?.eventCount ?? 'n/a'} />
            <MetricCard label="Connections" value={backendSse?.activeConnections ?? 'n/a'} />
            <MetricCard label="Last event" value={stream.lastEventType ?? backendSse?.lastEventType ?? 'none'} />
          </div>
        </div>
      </section>

      <DisclosureSection title="Endpoint and data coverage" defaultOpen meta={`${health?.endpoint_coverage.length ?? 0} surfaces`}>
        <CoverageTable rows={health?.endpoint_coverage ?? []} />
      </DisclosureSection>

      <DisclosureSection title="Recent events" defaultOpen meta={`${events.length} buffered`}>
        <Timeline events={events} limit={16} />
      </DisclosureSection>

      <DisclosureSection title="Logs and traces" meta={health?.logs.directory ?? 'not exposed'}>
        <div className="stacked-lines">
          <InfoLine label="Log directory" value={health?.logs.directory ?? 'not exposed'} />
          <InfoLine label="Human log" value={health?.logs.human_log_pattern ?? 'not exposed'} />
          <InfoLine label="Decision log" value={health?.logs.decision_log_pattern ?? 'not exposed'} />
          <InfoLine label="Replay corpus" value={replay?.pattern ?? 'not exposed'} />
          <InfoLine label="Replay files" value={replay ? `${replay.file_count} files / ${formatBytes(replay.total_bytes)}` : 'not exposed'} />
          <InfoLine label="Latest replay" value={formatMaybeDate(replay?.latest_write_at ?? null)} />
          <InfoLine label="Recent log endpoint" value={health?.logs.recent_endpoint ?? 'not exposed yet'} />
        </div>
        {replay && replay.files.length > 0 && (
          <div className="dense-table replay-table">
            <div className="dense-row header">
              <span>Minister</span>
              <span>File</span>
              <span>Size</span>
              <span>Updated</span>
            </div>
            {replay.files.map(file => (
              <div className="dense-row" key={file.path}>
                <span>{file.minister}</span>
                <span>{file.name}</span>
                <span>{formatBytes(file.size_bytes)}</span>
                <span>{formatMaybeDate(file.last_write_at)}</span>
              </div>
            ))}
          </div>
        )}
        {health && health.traces.length > 0 && (
          <div className="dense-table trace-table">
            <div className="dense-row header">
              <span>Minister</span>
              <span>Trigger</span>
              <span>Status</span>
              <span>Path</span>
            </div>
            {health.traces.map(trace => (
              <div className="dense-row" key={trace.minister}>
                <span>{trace.minister}</span>
                <span>{trace.trigger}</span>
                <span>{trace.status}</span>
                <span>{trace.path}</span>
              </div>
            ))}
          </div>
        )}
      </DisclosureSection>
    </div>
  );
}

function InfoLine({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="info-line">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function formatMaybeDate(iso: string | null): string {
  if (!iso) return 'none';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString();
}

function llmToneFor(status: string): 'ok' | 'warn' | 'error' | 'idle' {
  if (status === 'missing_key' || status === 'request_failed' || status === 'parse_failed') return 'error';
  if (status === 'ready' || status === 'not_seen_yet') return 'warn';
  if (status === 'parsed' || status === 'normalized') return 'ok';
  return 'warn';
}

function shorten(value: string): string {
  return value.length > 180 ? `${value.slice(0, 180)}...` : value;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
