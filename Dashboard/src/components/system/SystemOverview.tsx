import { useEffect, useState } from 'react';
import { fetchIconCacheStatus, startIconCacheWarm } from '../../api/icons';
import type { DashboardViewDefinition, DashboardViewKey } from '../../dashboard/scopes';
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
import { ViewTabs } from '../layout/ViewTabs';

type IconCacheFile = SystemHealth['icons']['files'][number];

export function SystemOverview({
  events,
  health,
  healthError,
  onSelectView,
  selectedView,
  status,
  stream,
  views,
}: {
  events: DashboardEvent[];
  health: SystemHealth | null;
  healthError: string | null;
  onSelectView: (view: DashboardViewKey) => void;
  selectedView: DashboardViewKey;
  status: RimBobStatus | null;
  stream: StreamDiagnostics;
  views: DashboardViewDefinition[];
}) {
  const [iconWarmAction, setIconWarmAction] = useState<{ status: 'idle' | 'pending' | 'ok' | 'error'; message: string | null }>({
    status: 'idle',
    message: null,
  });
  const backendSse = health?.sse;
  const llmStatus = health?.llm.status ?? status?.llm_status ?? ((status?.llm_configured ?? health?.llm.configured) ? 'ready' : 'missing_key');
  const llmTone = llmToneFor(llmStatus);
  const llmMetricTone = llmTone === 'idle' ? 'neutral' : llmTone;
  const healthIcons = health?.icons ?? null;
  const iconSummaryKey = healthIcons
    ? `${healthIcons.fileCount}:${healthIcons.totalBytes}:${healthIcons.latestWriteAt ?? 'none'}:${healthIcons.lastWarm?.completedAt ?? 'none'}`
    : null;
  const [iconInventory, setIconInventory] = useState<SystemHealth['icons'] | null>(null);
  const [iconInventoryKey, setIconInventoryKey] = useState<string | null>(null);
  const [iconInventoryStatus, setIconInventoryStatus] = useState<{ state: 'idle' | 'loading' | 'error'; message: string | null }>({
    state: 'idle',
    message: null,
  });
  const iconInventoryReady = selectedView === 'storage'
    && iconInventoryKey === iconSummaryKey
    && iconInventory?.filesIncluded === true;
  const icons = healthIcons
    ? {
        ...healthIcons,
        filesIncluded: iconInventoryReady ? true : healthIcons.filesIncluded,
        files: iconInventoryReady ? iconInventory.files : healthIcons.files,
      }
    : null;
  const warmJob = icons?.warmJob ?? null;
  const replay = health?.logs.replay_corpus;
  const tests = health?.tests;
  const liveTestCount = tests?.categories.find(category => category.category.toLowerCase() === 'live')?.count ?? 0;
  const rimapi = health?.rimapi_coverage;
  const colonySnapshot = health?.colony_snapshot;
  const storage = health?.storage;
  const ministerOutputs = health?.minister_outputs;
  const applyAttempts = health?.assisted_apply?.recent_attempts ?? [];
  const iconGroups = icons ? groupIconCacheFiles(icons.files) : [];
  const iconFailureState = icons ? summarizeIconWarmFailures(icons) : null;
  const warmRunning = warmJob?.state === 'running' || iconWarmAction.status === 'pending';

  useEffect(() => {
    if (selectedView !== 'storage' || !healthIcons || !iconSummaryKey) {
      return;
    }

    if (healthIcons.filesIncluded) {
      setIconInventory(healthIcons);
      setIconInventoryKey(iconSummaryKey);
      setIconInventoryStatus({ state: 'idle', message: null });
      return;
    }

    if (iconInventoryKey === iconSummaryKey && iconInventory?.filesIncluded) {
      return;
    }

    const controller = new AbortController();
    setIconInventoryStatus({ state: 'loading', message: 'Loading icon inventory...' });
    fetchIconCacheStatus(true, controller.signal)
      .then(status => {
        setIconInventory(status);
        setIconInventoryKey(iconSummaryKey);
        setIconInventoryStatus({ state: 'idle', message: null });
      })
      .catch(error => {
        if (controller.signal.aborted) return;
        setIconInventoryStatus({
          state: 'error',
          message: error instanceof Error ? error.message : 'Icon inventory could not be loaded.',
        });
      });

    return () => controller.abort();
  }, [healthIcons, iconInventory, iconInventoryKey, iconSummaryKey, selectedView]);

  async function onStartIconWarm(scope: 'all' | 'failed') {
    setIconWarmAction({ status: 'pending', message: scope === 'failed' ? 'Starting failed-only warm...' : 'Starting full warm...' });
    try {
      const status = await startIconCacheWarm(scope);
      setIconWarmAction({ status: 'ok', message: `Warm job ${status.state}: ${status.scope}` });
    } catch (error) {
      setIconWarmAction({ status: 'error', message: error instanceof Error ? error.message : 'Icon warm failed to start.' });
    }
  }

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

      <ViewTabs
        activeView={selectedView}
        ariaLabel="SYSTEM operations views"
        views={views}
        onSelect={onSelectView}
      />

      {selectedView === 'coverage' && (
      <div className="panel-registry-note">
        <span>Panel registry</span>
        {systemPanelRegistry.map(panel => (
          <code key={panel.id}>{panel.id}</code>
        ))}
      </div>
      )}

      {healthError && (
        <EmptyState code="SYSTEM HEALTH DEGRADED">{healthError}</EmptyState>
      )}

      {selectedView === 'connectivity' && (
      <DisclosureSection
        title={<SectionTitle iconKey="sse">Connectivity and providers</SectionTitle>}
        defaultOpen
        meta={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'RIMAPI reachable' : 'RIMAPI waiting'}
      >
        <section className="system-grid">
          <div className="system-card">
            <div className="section-heading">
              <span className="eyebrow">RIMAPI / LLM</span>
              <h2><SectionTitle iconKey="rimapi">External links</SectionTitle></h2>
            </div>
            <div className="metric-grid compact">
              <MetricCard label={<FieldLabel iconKey="rimapi">RIMAPI</FieldLabel>} value={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'reachable' : 'waiting'} tone={(status?.rimapi_reachable ?? health?.runtime.rimapi_reachable) ? 'ok' : 'warn'} />
              <MetricCard label={<FieldLabel iconKey="llm">LLM</FieldLabel>} value={llmStatus.replace(/_/g, ' ')} tone={llmMetricTone} />
              <MetricCard label={<FieldLabel iconKey="keys">Configured keys</FieldLabel>} value={(status?.llm_configured ?? health?.llm.configured) ? 'present' : 'missing'} tone={(status?.llm_configured ?? health?.llm.configured) ? 'ok' : 'error'} />
              <MetricCard label={<FieldLabel iconKey="last_event">Last LLM event</FieldLabel>} value={formatMaybeDate(health?.llm.last_event_at ?? status?.llm_last_event_at ?? null)} />
            </div>
            <div className="stacked-lines runtime-source-lines">
              <InfoLine label="Last LLM success" value={formatMaybeDate(health?.llm.last_success_at ?? status?.mayor_last_llm_success_at ?? null)} />
              <InfoLine label="Last LLM error" value={shorten(health?.llm.last_error ?? status?.llm_last_error ?? status?.mayor_last_error ?? 'none')} />
            </div>
          </div>

          <div className="system-card">
            <div className="section-heading">
              <span className="eyebrow">SSE</span>
              <h2><SectionTitle iconKey="sse">Advice stream</SectionTitle></h2>
            </div>
            <div className="metric-grid compact">
              <MetricCard label={<FieldLabel iconKey="client">Client</FieldLabel>} value={stream.state} tone={stream.state === 'open' ? 'ok' : stream.state === 'error' ? 'warn' : 'neutral'} />
              <MetricCard label={<FieldLabel iconKey="events">Client events</FieldLabel>} value={stream.eventCount} />
              <MetricCard label={<FieldLabel iconKey="server_events">Server events</FieldLabel>} value={backendSse?.eventCount ?? 'n/a'} />
              <MetricCard label={<FieldLabel iconKey="connections">Connections</FieldLabel>} value={backendSse?.activeConnections ?? 'n/a'} />
            </div>
            <div className="stacked-lines runtime-source-lines">
              <InfoLine label="Last client event" value={stream.lastEventType ?? 'none'} />
              <InfoLine label="Last server event" value={backendSse?.lastEventType ?? 'none'} />
              <InfoLine label="Last server error" value={backendSse?.lastError ?? 'none'} />
            </div>
          </div>
        </section>
      </DisclosureSection>
      )}

      {selectedView === 'coverage' && (
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
      )}

      {selectedView === 'runtime' && (
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
              <MetricCard label={<FieldLabel iconKey="agenda">Mayor snapshot</FieldLabel>} value={health?.runtime.mayor_snapshot_version ?? status?.mayor_snapshot_version ?? 'none'} />
              <MetricCard label={<FieldLabel iconKey="advice">Advice</FieldLabel>} value={health?.runtime.active_advice_count ?? 'n/a'} />
              <MetricCard label={<FieldLabel iconKey="flags">Flags</FieldLabel>} value={health?.runtime.active_flag_count ?? 'n/a'} />
              <MetricCard label={<FieldLabel iconKey="mayor">Mayor</FieldLabel>} value={status?.mayor_running ? 'running' : 'idle'} tone={status?.mayor_last_error ? 'error' : status?.mayor_running ? 'ok' : 'neutral'} />
            </div>
            <div className="stacked-lines runtime-source-lines">
              <InfoLine label="Running version" value={health?.version.running_version ?? health?.version.rim_bob_version ?? 'not exposed'} />
              <InfoLine label="Build number" value={health?.version.build_number ?? 'not exposed'} />
              <InfoLine label="Build datetime" value={formatMaybeDate(health?.version.build_datetime ?? null)} />
              <InfoLine label="Build revision" value={health?.version.build_revision_short ?? health?.version.build_version ?? 'not exposed'} />
              <InfoLine label="Dashboard assets" value={health?.version.dashboard_asset_version ?? 'not exposed'} />
              <InfoLine label="Reload token" value={shorten(health?.version.reload_token ?? 'not exposed')} />
              <InfoLine label="Host started" value={formatMaybeDate(health?.version.host_started_at ?? null)} />
              <InfoLine label="Host instance" value={health?.version.host_instance_id ?? 'not exposed'} />
              <InfoLine label="Host path" value={health?.runtime.host_process_path ?? 'not exposed'} />
              <InfoLine label="Content root" value={health?.runtime.content_root ?? 'not exposed'} />
              <InfoLine label="Runtime root" value={health?.runtime.runtime_root ?? 'not exposed'} />
              <InfoLine label="Data root" value={storage?.data_root ?? 'not exposed'} />
              <InfoLine label="Minister output root" value={storage?.minister_output_root ?? 'not exposed'} />
            </div>
          </div>

          <div className="system-card llm-card">
            <div className="section-heading">
              <span className="eyebrow">LLM</span>
              <h2><SectionTitle iconKey="llm">Gemini</SectionTitle></h2>
            </div>
            <div className="stacked-lines">
              <StatusPill tone={llmTone}>
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
              <InfoLine label="Cache root" value={health?.rag.cache_root ?? storage?.embedding_cache_root ?? 'not exposed'} />
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
      )}

      {selectedView === 'storage' && (
      <DisclosureSection
        title={<SectionTitle iconKey="snapshot">Minister outputs</SectionTitle>}
        meta={ministerOutputs ? `${ministerOutputs.snapshots.length} snapshots` : 'not exposed'}
      >
        {!ministerOutputs ? (
          <EmptyState code="MINISTER OUTPUTS MISSING">/api/system/health did not expose minister output metadata.</EmptyState>
        ) : (
          <div className="minister-output-panel">
            <div className="metric-grid compact">
              <MetricCard label={<FieldLabel iconKey="directory">Root</FieldLabel>} value={ministerOutputs.exists ? 'present' : 'missing'} tone={ministerOutputs.exists ? 'ok' : 'warn'} />
              <MetricCard label={<FieldLabel iconKey="snapshot">Snapshots</FieldLabel>} value={ministerOutputs.snapshots.length} />
            </div>
            <div className="stacked-lines">
              <InfoLine label="Root path" value={ministerOutputs.root_path ?? 'not configured'} />
            </div>
            {ministerOutputs.snapshots.length === 0 ? (
              <EmptyState code="NO MINISTER OUTPUTS">No persisted minister output snapshots exist yet.</EmptyState>
            ) : (
              <div className="dense-table minister-output-table">
                <div className="dense-row header">
                  <FieldLabel iconKey="minister">Minister</FieldLabel>
                  <FieldLabel iconKey="kind">Kind</FieldLabel>
                  <FieldLabel iconKey="version">Generation</FieldLabel>
                  <FieldLabel iconKey="updated">Persisted</FieldLabel>
                  <FieldLabel iconKey="status">State</FieldLabel>
                  <FieldLabel iconKey="path">Path</FieldLabel>
                </div>
                {ministerOutputs.snapshots.map(snapshot => (
                  <div className="dense-row" key={`${snapshot.minister}-${snapshot.output_kind}`}>
                    <span>{snapshot.minister}</span>
                    <span>{snapshot.output_kind}</span>
                    <span>{snapshot.generation ?? 'none'}</span>
                    <span>{formatMaybeDate(snapshot.persisted_at)}</span>
                    <span>{snapshot.last_error ? `${snapshot.state}: ${snapshot.last_error}` : snapshot.state}</span>
                    <span>{snapshot.path ?? 'not configured'}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </DisclosureSection>
      )}

      {selectedView === 'storage' && (
      <DisclosureSection
        title={<SectionTitle iconKey="snapshot">Colony snapshot</SectionTitle>}
        meta={colonySnapshot ? snapshotMeta(colonySnapshot, health?.runtime.rimapi_reachable ?? false) : 'not exposed'}
      >
        {!colonySnapshot ? (
          <EmptyState code="COLONY SNAPSHOT MISSING">/api/system/health did not expose colony snapshot metadata.</EmptyState>
        ) : (
          <div className="colony-snapshot-panel">
            <div className="metric-grid compact">
              <MetricCard
                label={<FieldLabel iconKey="snapshot">Snapshot</FieldLabel>}
                value={colonySnapshot.has_snapshot ? 'present' : 'missing'}
                tone={colonySnapshot.load_error ? 'error' : colonySnapshot.has_snapshot ? 'ok' : 'warn'}
              />
              <MetricCard
                label={<FieldLabel iconKey="status">Current origin</FieldLabel>}
                value={health?.runtime.colony_state_origin ?? status?.colony_state_origin ?? 'unknown'}
                tone={health?.runtime.rimapi_reachable ? 'ok' : colonySnapshot.has_snapshot ? 'warn' : 'neutral'}
              />
              <MetricCard label={<FieldLabel iconKey="captured_at">Captured</FieldLabel>} value={formatMaybeDate(colonySnapshot.captured_at)} />
              <MetricCard label={<FieldLabel iconKey="age">Age</FieldLabel>} value={formatAgeSeconds(colonySnapshot.age_seconds)} />
              <MetricCard label={<FieldLabel iconKey="game_tick">Game tick</FieldLabel>} value={colonySnapshot.game_tick?.toLocaleString() ?? 'none'} />
              <MetricCard label={<FieldLabel iconKey="map">Map</FieldLabel>} value={colonySnapshot.map_id ?? 'none'} />
            </div>
            <div className="stacked-lines">
              <InfoLine label="Path" value={colonySnapshot.path ?? 'not configured'} />
              <InfoLine label="Configured path" value={storage?.colony_state_snapshot_path ?? 'not exposed'} />
              <InfoLine label="Snapshot id" value={colonySnapshot.snapshot_id ?? 'none'} />
              <InfoLine label="Capture source" value={colonySnapshot.source ?? 'none'} />
              <InfoLine label="Schema" value={colonySnapshot.schema_version ?? 'none'} />
              <InfoLine label="Last live refresh" value={formatMaybeDate(health?.runtime.last_live_refresh_at ?? null)} />
              <InfoLine label="Last save" value={formatMaybeDate(colonySnapshot.last_save_at)} />
              <InfoLine label="Last save error" value={colonySnapshot.last_save_error ?? 'none'} />
              <InfoLine label="Load error" value={colonySnapshot.load_error ?? 'none'} />
            </div>
          </div>
        )}
      </DisclosureSection>
      )}

      {selectedView === 'events' && (
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
      )}

      {selectedView === 'coverage' && (
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
      )}

      {selectedView === 'storage' && (
      <DisclosureSection
        title={<SectionTitle iconKey="icon_cache">Icon cache</SectionTitle>}
        meta={icons ? `${icons.fileCount} cached PNGs | ${warmJob?.state ?? 'idle'}` : 'not exposed'}
      >
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
                note={icons.lastWarm ? `${icons.lastWarm.failed} failed / ${icons.lastWarm.deferred} deferred` : undefined}
                tone={!icons.lastWarm ? 'warn' : icons.lastWarm.failed > 0 ? 'warn' : 'ok'}
              />
              <MetricCard
                label={<FieldLabel iconKey="status">Warm job</FieldLabel>}
                value={warmJob?.state ?? 'idle'}
                note={warmJob && warmJob.total > 0 ? `${warmJob.done}/${warmJob.total} ${warmJob.scope}` : warmJob?.scope}
                tone={warmJobTone(warmJob?.state)}
              />
              <MetricCard
                label={<FieldLabel iconKey="missing">Missing failures</FieldLabel>}
                value={icons.lastWarm?.missingFailures ?? 0}
                tone={(icons.lastWarm?.missingFailures ?? 0) > 0 ? 'warn' : 'ok'}
              />
              <MetricCard
                label={<FieldLabel iconKey="transport">Transport failures</FieldLabel>}
                value={icons.lastWarm?.transportFailures ?? 0}
                tone={(icons.lastWarm?.transportFailures ?? 0) > 0 ? 'warn' : 'ok'}
              />
            </div>
            <div className="icon-warm-actions">
              <button
                type="button"
                className="trigger-button"
                disabled={warmRunning}
                onClick={() => void onStartIconWarm('all')}
              >
                {warmRunning && warmJob?.scope === 'all' ? 'Warming...' : 'Warm all'}
              </button>
              <button
                type="button"
                className="trigger-button"
                disabled={warmRunning || (icons.lastWarm?.failed ?? 0) === 0}
                onClick={() => void onStartIconWarm('failed')}
              >
                {warmRunning && warmJob?.scope === 'failed' ? 'Re-warming...' : 'Re-warm failed'}
              </button>
              {iconWarmAction.message && (
                <small className={`icon-warm-action-status ${iconWarmAction.status}`}>
                  {iconWarmAction.message}
                </small>
              )}
            </div>
            <div className="stacked-lines">
              <InfoLine label="Directory" value={icons.directory} />
              <InfoLine label="Latest write" value={formatMaybeDate(icons.latestWriteAt)} />
              <InfoLine label="Job id" value={warmJob?.jobId ?? 'none'} />
              <InfoLine label="Job updated" value={formatMaybeDate(warmJob?.updatedAt ?? null)} />
              <InfoLine label="Job error" value={warmJob?.error ?? 'none'} />
              <InfoLine label="Skipped" value={icons.lastWarm?.skipped ?? 'n/a'} />
              <InfoLine label="Failures" value={icons.lastWarm?.failed ?? 'n/a'} />
              <InfoLine label="Deferred" value={icons.lastWarm?.deferred ?? 'n/a'} />
              {iconFailureState && iconFailureState.resolvedSampleCount > 0 && (
                <InfoLine
                  label="Cached after failure sample"
                  value={`${iconFailureState.resolvedSampleCount}/${iconFailureState.sampleCount}`}
                />
              )}
              <InfoLine label="By kind" value={formatKindCounts(icons.filesByKind)} />
            </div>
            {!icons.filesIncluded ? (
              <EmptyState code={iconInventoryStatus.state === 'error' ? 'ICON INVENTORY ERROR' : 'ICON INVENTORY SUMMARY'}>
                {iconInventoryStatus.message ?? 'Full cached PNG inventory is loaded only for this SYSTEM storage view.'}
              </EmptyState>
            ) : icons.files.length === 0 ? (
              <EmptyState code="ICON CACHE EMPTY">No cached PNG files exist under the icon cache directory.</EmptyState>
            ) : (
              <div className="icon-cache-groups" aria-label="Cached icon files">
                {iconGroups.map(group => (
                  <section className="icon-cache-group" key={group.key}>
                    <div className="icon-cache-group-header">
                      <strong>{group.label}</strong>
                      <span>{group.files.length}</span>
                    </div>
                    <div className="icon-cache-strip">
                      {group.files.map(file => (
                        <div
                          className="icon-cache-tile"
                          key={file.relativePath}
                          title={`${file.kind}: ${file.id} | ${file.relativePath} | ${formatBytes(file.sizeBytes)}`}
                        >
                          <GameIcon
                            fallback={file.id.slice(0, 1).toUpperCase()}
                            label={`${file.id} cached icon`}
                            size="sm"
                            src={file.publicPath}
                          />
                        </div>
                      ))}
                    </div>
                  </section>
                ))}
              </div>
            )}
            {iconFailureState && iconFailureState.activeIssues.length > 0 && (
              <div className="dense-table icon-failure-table">
                <div className="dense-row header">
                  <FieldLabel iconKey="status">Status</FieldLabel>
                  <FieldLabel iconKey="kind">Kind</FieldLabel>
                  <FieldLabel iconKey="id">Id</FieldLabel>
                  <FieldLabel iconKey="error">Error</FieldLabel>
                </div>
                {iconFailureState.activeIssues.slice(0, 12).map(failure => (
                  <div className="dense-row" key={`${failure.kind}-${failure.id}-${failure.error}`}>
                    <span>{failure.status}</span>
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
      )}

      {selectedView === 'coverage' && (
      <DisclosureSection title={<SectionTitle iconKey="endpoint_coverage">Endpoint and data coverage</SectionTitle>} meta={`${health?.endpoint_coverage.length ?? 0} surfaces`}>
        <CoverageTable rows={health?.endpoint_coverage ?? []} />
      </DisclosureSection>
      )}

      {selectedView === 'events' && (
      <DisclosureSection title={<SectionTitle iconKey="recent_events">Recent events</SectionTitle>} meta={`${events.length} buffered`}>
        <Timeline events={events} limit={16} />
      </DisclosureSection>
      )}

      {selectedView === 'events' && (
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
      )}
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

function summarizeIconWarmFailures(icons: SystemHealth['icons']) {
  if (!icons.lastWarm || icons.lastWarm.failures.length === 0) {
    return null;
  }

  const cached = new Set(icons.files.map(file => iconFailureKey(file.kind, file.id)));
  const activeIssues = icons.lastWarm.failures.filter(failure => {
    if (failure.status === 'failed') {
      return !cached.has(iconFailureKey(failure.kind, failure.id));
    }

    return true;
  });

  return {
    resolvedSampleCount: icons.lastWarm.failures.length - activeIssues.length,
    sampleCount: icons.lastWarm.failures.length,
    activeIssues,
  };
}

function iconFailureKey(kind: string, id: string): string {
  return `${kind}:${id}`.toLowerCase();
}

function groupIconCacheFiles(files: IconCacheFile[]) {
  const groups = new Map<string, { key: string; label: string; files: IconCacheFile[] }>();

  for (const file of files) {
    const group = iconCacheGroupFor(file);
    const existing = groups.get(group.key);
    if (existing) {
      existing.files.push(file);
    } else {
      groups.set(group.key, { ...group, files: [file] });
    }
  }

  return iconCacheGroupOrder
    .map(key => groups.get(key))
    .filter((group): group is { key: string; label: string; files: IconCacheFile[] } => Boolean(group))
    .map(group => ({
      ...group,
      files: [...group.files].sort(compareIconCacheFiles),
    }));
}

const iconCacheGroupOrder = [
  'food',
  'resources',
  'buildings',
  'weapons',
  'apparel',
  'medical',
  'animals',
  'terrain',
  'pawns',
  'other',
];

function iconCacheGroupFor(file: IconCacheFile): { key: string; label: string } {
  if (file.kind === 'pawn-portrait') return { key: 'pawns', label: 'Pawns' };
  if (file.kind === 'terrain') return { key: 'terrain', label: 'Terrain' };

  const id = file.id.toLowerCase();
  if (
    id.startsWith('plant_') ||
    id.startsWith('raw') ||
    id.startsWith('meat_') ||
    id.includes('meal') ||
    id.includes('pemmican') ||
    id.includes('kibble') ||
    id.includes('nutrition')
  ) return { key: 'food', label: 'Food & crops' };

  if (
    id.includes('steel') ||
    id.includes('silver') ||
    id.includes('gold') ||
    id.includes('plasteel') ||
    id.includes('wood') ||
    id.includes('component') ||
    id.includes('leather') ||
    id.includes('cloth') ||
    id.includes('chemfuel') ||
    id.includes('stoneblock')
  ) return { key: 'resources', label: 'Resources' };

  if (
    id.includes('bench') ||
    id.includes('stove') ||
    id.includes('table') ||
    id.includes('cooler') ||
    id.includes('generator') ||
    id.includes('battery') ||
    id.includes('conduit') ||
    id.includes('comms') ||
    id.includes('beacon') ||
    id.includes('door') ||
    id.includes('wall') ||
    id.includes('bed') ||
    id.includes('campfire') ||
    id.includes('refinery')
  ) return { key: 'buildings', label: 'Buildings & production' };

  if (
    id.startsWith('gun_') ||
    id.startsWith('bow_') ||
    id.startsWith('meleeweapon_') ||
    id.includes('turret') ||
    id.includes('mortar') ||
    id.includes('trap') ||
    id.includes('shieldbelt') ||
    id.includes('lance')
  ) return { key: 'weapons', label: 'Weapons & defense' };

  if (id.startsWith('apparel_')) return { key: 'apparel', label: 'Apparel' };

  if (
    id.includes('medicine') ||
    id.includes('bionic') ||
    id.includes('archotech') ||
    id.includes('prosthetic') ||
    id.includes('heart') ||
    id.includes('kidney') ||
    id.includes('lung')
  ) return { key: 'medical', label: 'Medical' };

  if (id.startsWith('corpse_') || id.includes('hare') || id.includes('alpaca')) {
    return { key: 'animals', label: 'Animals & corpses' };
  }

  return { key: 'other', label: 'Other cached defs' };
}

function compareIconCacheFiles(left: IconCacheFile, right: IconCacheFile): number {
  return left.id.localeCompare(right.id, undefined, { sensitivity: 'base' });
}

function formatMaybeDate(iso: string | null): string {
  if (!iso) return 'none';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString();
}

function snapshotMeta(snapshot: SystemHealth['colony_snapshot'], rimapiReachable: boolean): string {
  if (snapshot.load_error) return 'load error';
  if (!snapshot.has_snapshot) return 'no snapshot';
  if (!rimapiReachable) return `stale ${formatAgeSeconds(snapshot.age_seconds)}`;
  return `saved ${formatAgeSeconds(snapshot.age_seconds)} ago`;
}

function formatAgeSeconds(seconds: number | null): string {
  if (seconds == null) return 'none';
  if (seconds < 60) return `${Math.max(0, Math.round(seconds))}s`;

  const minutes = seconds / 60;
  if (minutes < 60) return `${Math.round(minutes)}m`;

  const hours = minutes / 60;
  if (hours < 48) return `${hours.toFixed(1)}h`;

  return `${Math.round(hours / 24)}d`;
}

function llmToneFor(status: string): 'ok' | 'warn' | 'error' | 'idle' {
  if (status === 'missing_key' || status === 'request_failed' || status === 'parse_failed') return 'error';
  if (status === 'ready' || status === 'not_seen_yet') return 'warn';
  if (status === 'parsed' || status === 'normalized') return 'ok';
  return 'warn';
}

function warmJobTone(status: string | null | undefined): 'neutral' | 'ok' | 'warn' | 'error' {
  if (status === 'completed') return 'ok';
  if (status === 'failed') return 'error';
  if (status === 'running') return 'warn';
  return 'neutral';
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
