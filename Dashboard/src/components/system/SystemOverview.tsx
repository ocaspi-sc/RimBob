import type { RimBobStatus } from '../../types/status';
import type { DashboardEvent, RimApiCoverageRow, StreamDiagnostics, SystemHealth } from '../../types/system';
import { iconForField, iconForSection, iconForScope } from '../../dashboard/semanticIcons';
import { systemPanelRegistry } from '../../dashboard/panelRegistry';
import { CoverageTable } from '../shared/DataCoverage';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { MetricCard } from '../shared/MetricCard';
import { SemanticLabel } from '../shared/SemanticIcon';
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
  status: RimBobStatus | null;
  stream: StreamDiagnostics;
}) {
  const backendSse = health?.sse;
  const llmStatus = health?.llm.status ?? status?.llm_status ?? ((status?.llm_configured ?? health?.llm.configured) ? 'ready' : 'missing_key');
  const icons = health?.icons;
  const replay = health?.logs.replay_corpus;
  const tests = health?.tests;
  const liveTestCount = tests?.categories.find(category => category.category.toLowerCase() === 'live')?.count ?? 0;
  const rimapi = health?.rimapi_coverage;
  const applyAttempts = health?.assisted_apply?.recent_attempts ?? [];

  return (
    <div className="system-overview">
      <header className="system-hero system-card">
        <div>
          <span className="eyebrow">Operations</span>
          <h2><SemanticLabel icon={iconForScope('system')} size="sm"><span>SYSTEM</span></SemanticLabel></h2>
          <p>Runtime diagnostics for Host, RIMAPI, SSE transport, LLM provider state, logs, traces, icons, tests, and endpoint coverage.</p>
        </div>
        <div className="scope-boundary-strip">
          <span>Debug</span>
          <span>Raw health</span>
          <span>Endpoints</span>
          <span>Logs</span>
        </div>
      </header>

      <div className="panel-registry-note">
        <span>Panel registry</span>
        {systemPanelRegistry.map(panel => (
          <code key={panel.id}>{panel.id}</code>
        ))}
      </div>

      {healthError && (
        <EmptyState code="SYSTEM HEALTH DEGRADED">{healthError}</EmptyState>
      )}

      <DisclosureSection title={<SectionTitle iconKey="tests">Test inventory</SectionTitle>} meta={tests ? `${tests.total_count} declared tests` : 'not exposed'}>
        {!tests ? (
          <EmptyState code="TEST INVENTORY MISSING">/api/system/health did not expose test metadata.</EmptyState>
        ) : (
          <div className="test-inventory-panel">
            <div className="metric-grid compact">
              <MetricCard label={<FieldLabel iconKey="tests">Declared tests</FieldLabel>} value={tests.total_count} />
              <MetricCard label={<FieldLabel iconKey="categories">Categories</FieldLabel>} value={tests.categories.length} />
              <MetricCard label={<FieldLabel iconKey="files">Test files</FieldLabel>} value={tests.file_count} />
              <MetricCard label={<FieldLabel iconKey="live">Live-gated</FieldLabel>} value={liveTestCount} tone={liveTestCount > 0 ? 'warn' : 'neutral'} />
            </div>
            {tests.scan_error && (
              <EmptyState code="TEST SCAN DEGRADED">{tests.scan_error}</EmptyState>
            )}
            <div className="dense-table test-table">
              <div className="dense-row header">
                <FieldLabel iconKey="category">Category</FieldLabel>
                <FieldLabel iconKey="tests">Tests</FieldLabel>
                <FieldLabel iconKey="files">Files</FieldLabel>
              </div>
              {tests.categories.map(category => (
                <div className="dense-row" key={category.category}>
                  <span>{category.category}</span>
                  <span>{category.count}</span>
                  <span>{category.file_count}</span>
                </div>
              ))}
            </div>
            <div className="stacked-lines test-source-lines">
              <InfoLine label="Source" value={tests.source} />
              <InfoLine label="Project" value={tests.project} />
            </div>
          </div>
        )}
      </DisclosureSection>

      <DisclosureSection
        title={<SectionTitle iconKey="runtime">Runtime diagnostics</SectionTitle>}
        meta={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'RIMAPI reachable' : 'RIMAPI waiting'}
      >
        <section className="system-grid">
          <div className="system-card runtime-card">
            <div className="section-heading">
              <span className="eyebrow">Runtime</span>
              <h2><SectionTitle iconKey="host">Host Loop</SectionTitle></h2>
            </div>
            <div className="metric-grid">
              <MetricCard label={<FieldLabel iconKey="host">Host</FieldLabel>} value={status?.server ?? health?.runtime.server ?? 'checking'} tone={status ? 'ok' : 'neutral'} />
              <MetricCard label={<FieldLabel iconKey="rimapi">RIMAPI</FieldLabel>} value={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'reachable' : 'waiting'} tone={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'ok' : 'warn'} />
              <MetricCard label={<FieldLabel iconKey="agenda">Agenda</FieldLabel>} value={health?.runtime.agenda_version ?? status?.agenda_version ?? 'none'} />
              <MetricCard label={<FieldLabel iconKey="advice">Advice</FieldLabel>} value={health?.runtime.active_advice_count ?? 'n/a'} />
              <MetricCard label={<FieldLabel iconKey="flags">Flags</FieldLabel>} value={health?.runtime.active_flag_count ?? 'n/a'} />
              <MetricCard label={<FieldLabel iconKey="mayor">Mayor</FieldLabel>} value={status?.mayor_running ? 'running' : 'idle'} tone={status?.mayor_last_error ? 'error' : status?.mayor_running ? 'ok' : 'neutral'} />
            </div>
          </div>

          <div className="system-card llm-card">
            <div className="section-heading">
              <span className="eyebrow">LLM</span>
              <h2><SectionTitle iconKey="llm">Gemini</SectionTitle></h2>
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
              <h2><SectionTitle iconKey="rag">Knowledge</SectionTitle></h2>
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
              <h2><SectionTitle iconKey="sse">SSE Diagnostics</SectionTitle></h2>
            </div>
            <div className="metric-grid compact">
              <MetricCard label={<FieldLabel iconKey="client">Client</FieldLabel>} value={stream.state} tone={stream.state === 'open' ? 'ok' : stream.state === 'error' ? 'warn' : 'neutral'} />
              <MetricCard label={<FieldLabel iconKey="events">Client events</FieldLabel>} value={stream.eventCount} />
              <MetricCard label={<FieldLabel iconKey="reconnects">Reconnects</FieldLabel>} value={stream.reconnectCount} tone={stream.reconnectCount > 0 ? 'warn' : 'neutral'} />
              <MetricCard label={<FieldLabel iconKey="server_events">Server events</FieldLabel>} value={backendSse?.eventCount ?? 'n/a'} />
              <MetricCard label={<FieldLabel iconKey="connections">Connections</FieldLabel>} value={backendSse?.activeConnections ?? 'n/a'} />
              <MetricCard label={<FieldLabel iconKey="last_event">Last event</FieldLabel>} value={stream.lastEventType ?? backendSse?.lastEventType ?? 'none'} />
            </div>
          </div>
        </section>
      </DisclosureSection>

      <DisclosureSection title={<SectionTitle iconKey="assisted_apply">Assisted Apply</SectionTitle>} meta={`${applyAttempts.length} recent attempts`}>
        {applyAttempts.length === 0 ? (
          <EmptyState code="NO APPLY ATTEMPTS">No Assisted Apply attempts have been recorded this session.</EmptyState>
        ) : (
          <div className="dense-table assisted-apply-table">
            <div className="dense-row header">
              <FieldLabel iconKey="updated">Time</FieldLabel>
              <FieldLabel iconKey="kind">Kind</FieldLabel>
              <FieldLabel iconKey="status">Status</FieldLabel>
              <FieldLabel iconKey="advice">Advice</FieldLabel>
              <FieldLabel iconKey="message">Message</FieldLabel>
            </div>
            {applyAttempts.map(attempt => (
              <div className="dense-row" key={`${attempt.at}-${attempt.advice_id}-${attempt.action_index}`}>
                <span>{formatMaybeDate(attempt.at)}</span>
                <span>{formatKind(attempt.kind)}</span>
                <span>{attempt.status.replace(/_/g, ' ')}</span>
                <code>{attempt.advice_id}#{attempt.action_index}</code>
                <span>{attempt.message}</span>
              </div>
            ))}
          </div>
        )}
      </DisclosureSection>

      <DisclosureSection title={<SectionTitle iconKey="rimapi">RIMAPI integration snapshot</SectionTitle>} meta={rimapi ? `${rimapi.active_read_count}/${rimapi.cached_upstream_endpoint_total} cached endpoints` : 'not exposed'}>
        {!rimapi ? (
          <EmptyState code="RIMAPI COVERAGE MISSING">/api/system/health did not expose RIMAPI coverage metadata.</EmptyState>
        ) : (
          <div className="rimapi-coverage-panel">
            <div className="metric-grid compact">
              <MetricCard
                label={<FieldLabel iconKey="active_reads">Declared active reads</FieldLabel>}
                value={`${rimapi.active_read_count} / ${rimapi.cached_upstream_endpoint_total}`}
                note={`${rimapi.active_read_percent}% of cached upstream`}
                tone={rimapi.active_read_count > 0 ? 'ok' : 'warn'}
              />
              <MetricCard
                label={<FieldLabel iconKey="client">Represented in client</FieldLabel>}
                value={`${rimapi.represented_endpoint_count} / ${rimapi.cached_upstream_endpoint_total}`}
                note={`${rimapi.represented_endpoint_percent}% including stubs`}
              />
              <MetricCard label={<FieldLabel iconKey="client_methods">Client methods</FieldLabel>} value={rimapi.client_method_count} />
              <MetricCard label={<FieldLabel iconKey="deferred_writes">Deferred writes</FieldLabel>} value={rimapi.deferred_write_stub_count} tone="warn" />
            </div>
            <div className="stacked-lines rimapi-source-lines">
              <InfoLine label="Basis" value={rimapi.coverage_basis} />
              <InfoLine label="Source" value={rimapi.source} />
              <InfoLine label="Caveat" value={rimapi.coverage_note} />
            </div>
            <RimApiCoverageTable title="Active reads" rows={rimapi.active_reads} />
            <RimApiCoverageTable title="Represented, not refreshed" rows={rimapi.represented_not_refreshed} />
            <RimApiCoverageTable title="Deferred write stubs" rows={rimapi.deferred_writes} />
            <RimApiCoverageTable title="Missing priorities" rows={rimapi.missing_priorities} />
          </div>
        )}
      </DisclosureSection>

      <DisclosureSection title={<SectionTitle iconKey="icon_cache">Icon cache</SectionTitle>} meta={icons ? `${icons.fileCount} cached PNGs` : 'not exposed'}>
        {!icons ? (
          <EmptyState code="ICON CACHE MISSING">/api/system/health did not expose icon cache metadata.</EmptyState>
        ) : (
          <div className="icon-cache-panel">
            <div className="metric-grid compact">
              <MetricCard label={<FieldLabel iconKey="files">Files</FieldLabel>} value={icons.fileCount} tone={icons.fileCount > 0 ? 'ok' : 'warn'} />
              <MetricCard label={<FieldLabel iconKey="bytes">Bytes</FieldLabel>} value={formatBytes(icons.totalBytes)} />
              <MetricCard label={<FieldLabel iconKey="kinds">Kinds</FieldLabel>} value={Object.keys(icons.filesByKind).length} />
              <MetricCard
                label={<FieldLabel iconKey="warm_result">Warm result</FieldLabel>}
                value={icons.lastWarm ? `${icons.lastWarm.succeeded}/${icons.lastWarm.totalCandidates}` : 'not run'}
                tone={!icons.lastWarm ? 'warn' : icons.lastWarm.failed > 0 ? 'warn' : 'ok'}
              />
            </div>
            <div className="stacked-lines">
              <InfoLine label="Directory" value={icons.directory} />
              <InfoLine label="Latest write" value={formatMaybeDate(icons.latestWriteAt)} />
              <InfoLine label="Skipped" value={icons.lastWarm?.skipped ?? 'n/a'} />
              <InfoLine label="Failures" value={icons.lastWarm?.failed ?? 'n/a'} />
              <InfoLine label="By kind" value={formatKindCounts(icons.filesByKind)} />
            </div>
            {icons.files.length === 0 ? (
              <EmptyState code="ICON CACHE EMPTY">No cached PNG files exist under the icon cache directory.</EmptyState>
            ) : (
              <div className="icon-cache-strip" aria-label="Cached icon files">
                {icons.files.map(file => (
                  <div
                    className="icon-cache-tile"
                    key={file.relativePath}
                    title={`${file.kind}: ${file.id} | ${file.relativePath} | ${formatBytes(file.sizeBytes)}`}
                  >
                    <GameIcon
                      fallback={file.id.slice(0, 1).toUpperCase()}
                      label={`${file.id} cached icon`}
                      size="md"
                      src={file.publicPath}
                    />
                    <code>{file.id}</code>
                  </div>
                ))}
              </div>
            )}
            {icons.lastWarm && icons.lastWarm.failures.length > 0 && (
              <div className="dense-table icon-failure-table">
                <div className="dense-row header">
                  <FieldLabel iconKey="kind">Kind</FieldLabel>
                  <FieldLabel iconKey="id">Id</FieldLabel>
                  <FieldLabel iconKey="error">Error</FieldLabel>
                </div>
                {icons.lastWarm.failures.slice(0, 12).map(failure => (
                  <div className="dense-row" key={`${failure.kind}-${failure.id}-${failure.error}`}>
                    <span>{failure.kind}</span>
                    <code>{failure.id}</code>
                    <span>{failure.error}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </DisclosureSection>

      <DisclosureSection title={<SectionTitle iconKey="endpoint_coverage">Endpoint and data coverage</SectionTitle>} meta={`${health?.endpoint_coverage.length ?? 0} surfaces`}>
        <CoverageTable rows={health?.endpoint_coverage ?? []} />
      </DisclosureSection>

      <DisclosureSection title={<SectionTitle iconKey="recent_events">Recent events</SectionTitle>} meta={`${events.length} buffered`}>
        <Timeline events={events} limit={16} />
      </DisclosureSection>

      <DisclosureSection title={<SectionTitle iconKey="logs">Logs and traces</SectionTitle>} meta={health?.logs.directory ?? 'not exposed'}>
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
              <FieldLabel iconKey="minister">Minister</FieldLabel>
              <FieldLabel iconKey="file">File</FieldLabel>
              <FieldLabel iconKey="size">Size</FieldLabel>
              <FieldLabel iconKey="updated">Updated</FieldLabel>
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
              <FieldLabel iconKey="minister">Minister</FieldLabel>
              <FieldLabel iconKey="trigger">Trigger</FieldLabel>
              <FieldLabel iconKey="status">Status</FieldLabel>
              <FieldLabel iconKey="path">Path</FieldLabel>
              <FieldLabel iconKey="note">Detail</FieldLabel>
            </div>
            {health.traces.map(trace => (
              <div className="dense-row" key={trace.minister}>
                <span>{trace.minister}</span>
                <span>{trace.trigger}</span>
                <span>{trace.status}</span>
                <span>{formatKind(trace.path)}</span>
                <span>{formatTraceDetail(trace)}</span>
              </div>
            ))}
          </div>
        )}
      </DisclosureSection>
    </div>
  );
}

function RimApiCoverageTable({ rows, title }: { rows: RimApiCoverageRow[]; title: string }) {
  return (
    <section className="rimapi-coverage-section">
      <div className="rimapi-table-heading">
        <SectionTitle iconKey="rimapi">{title}</SectionTitle>
        <small>{rows.length} rows</small>
      </div>
      <div className="dense-table rimapi-table">
        <div className="dense-row header">
          <FieldLabel iconKey="method">Method</FieldLabel>
          <FieldLabel iconKey="endpoint">Endpoint</FieldLabel>
          <FieldLabel iconKey="state">State</FieldLabel>
          <FieldLabel iconKey="owner">Owner</FieldLabel>
          <FieldLabel iconKey="note">Note</FieldLabel>
        </div>
        {rows.map(row => (
          <div className="dense-row" key={`${row.method}-${row.endpoint}-${row.state}`}>
            <span>{row.method}</span>
            <code>{row.endpoint}</code>
            <span>{row.state}</span>
            <span>{row.owner}</span>
            <span>{row.note}</span>
          </div>
        ))}
      </div>
    </section>
  );
}

function SectionTitle({ children, iconKey }: { children: string; iconKey: string }) {
  return <SemanticLabel icon={iconForSection(iconKey)}><span>{children}</span></SemanticLabel>;
}

function FieldLabel({ children, iconKey }: { children: string; iconKey: string }) {
  return <SemanticLabel icon={iconForField(iconKey)}><span>{children}</span></SemanticLabel>;
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

function formatKind(value: string | null | undefined): string {
  return value ? value.replace(/_/g, ' ') : '-';
}

function formatTraceDetail(trace: SystemHealth['traces'][number]): string {
  if (trace.errorMessage) return `${trace.errorType ?? 'error'}: ${trace.errorMessage}`;
  if (trace.ruleFired) return `rule: ${trace.ruleFired}`;
  if (trace.escalationReason) return `reason: ${trace.escalationReason}`;

  const counts = [
    trace.adviceCount !== null ? `${trace.adviceCount} advice` : null,
    trace.flagCount !== null ? `${trace.flagCount} flags` : null,
  ].filter(Boolean).join(' / ');

  return counts || trace.note;
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function formatKindCounts(values: Record<string, number>): string {
  const entries = Object.entries(values);
  if (entries.length === 0) return 'none';
  return entries
    .map(([kind, count]) => `${kind}: ${count}`)
    .join(', ');
}
