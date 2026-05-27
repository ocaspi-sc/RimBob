import type { RimBobStatus } from '../../types/status';
import type { RimBobRunningVersion, StreamDiagnostics } from '../../types/system';
import { StatusPill, type PillTone } from '../shared/StatusPill';

export function DashboardHeader({
  hostProcessPath,
  runtimeRoot,
  onTriggerCabinet,
  status,
  statusError,
  statusLoadedAt,
  stream,
  triggerDisabled,
  triggerError,
  triggerPending,
  version,
}: {
  hostProcessPath: string | null;
  runtimeRoot: string | null;
  onTriggerCabinet: () => void;
  status: RimBobStatus | null;
  statusError: string | null;
  statusLoadedAt: string | null;
  stream: StreamDiagnostics;
  triggerDisabled: boolean;
  triggerError: string | null;
  triggerPending: boolean;
  version: RimBobRunningVersion | null;
}) {
  const hostState = deriveHostApiState(status, statusError, statusLoadedAt);
  const rimApiState = deriveRimApiState(status, hostState);
  const llmState = deriveLlmState(status, hostState);
  const mayorState = deriveMayorState(status, hostState);
  const streamState = deriveStreamState(stream);
  const versionMarker = version ? `RimBob ${version.running_version}` : 'RimBob checking';
  const revisionMarker = version ? `rev ${version.build_revision_short ?? version.build_version}` : 'rev checking';
  const buildMarker = version ? `built ${formatBuildDateTime(version.build_datetime)}` : 'built checking';
  const assetMarker = version ? `UI ${dashboardAssetLabel(version.dashboard_asset_version)}` : 'UI checking';
  const rootMarker = runtimeRoot ? `root ${shortPath(runtimeRoot, 3)}` : 'root checking';

  return (
    <header className="dashboard-header panel-shell">
      <div className="brand-block">
        <span className="eyebrow">RimWorld Advisory Cabinet</span>
        <h1>RimBob Dashboard v2</h1>
        <div className="running-version" aria-label="Running RimBob version">
          <span title={version ? `source=/api/system/health; version.running_version=${version.running_version}` : 'source=/api/system/health; version.running_version=checking'}>
            {versionMarker}
          </span>
          <code title={version ? `source=/api/system/health; version.build_revision=${version.build_revision ?? version.build_version}` : 'source=/api/system/health; version.build_revision=checking'}>
            {revisionMarker}
          </code>
          <span title={version ? `source=/api/system/health; version.build_datetime=${formatBuildDateTime(version.build_datetime)}` : 'source=/api/system/health; version.build_datetime=checking'}>
            {buildMarker}
          </span>
          <span title={version ? `source=/api/system/health; version.dashboard_asset_version=${version.dashboard_asset_version}` : 'source=/api/system/health; version.dashboard_asset_version=checking'}>
            {assetMarker}
          </span>
          <span title={runtimeTooltip(runtimeRoot, hostProcessPath)}>
            {rootMarker}
          </span>
        </div>
      </div>
      <div className="header-controls">
        <div className="header-status">
          <StatusPill tone={hostState.tone} title={hostState.title}>Host API {hostState.label}</StatusPill>
          <StatusPill tone={rimApiState.tone} title={rimApiState.title}>RIMAPI {rimApiState.label}</StatusPill>
          <StatusPill tone={llmState.tone} title={llmState.title}>LLM {llmState.label}</StatusPill>
          <StatusPill tone={streamState.tone} title={streamState.title}>SSE {streamState.label}</StatusPill>
          <StatusPill tone={mayorState.tone} title={mayorState.title}>Mayor {mayorState.label}</StatusPill>
        </div>
        <button
          type="button"
          className="trigger-button global"
          disabled={triggerDisabled}
          aria-busy={triggerPending}
          onClick={onTriggerCabinet}
          title="Trigger all live ministers manually"
        >
          {triggerPending ? 'Running Cabinet...' : 'Run Cabinet Now'}
        </button>
        {triggerError && <span className="trigger-error" role="status">{triggerError}</span>}
      </div>
    </header>
  );
}

type HostApiKind = 'checking' | 'live' | 'stale' | 'offline';

interface HeaderState {
  label: string;
  tone: PillTone;
  title: string;
}

interface HostApiState extends HeaderState {
  kind: HostApiKind;
}

function deriveHostApiState(
  status: RimBobStatus | null,
  statusError: string | null,
  statusLoadedAt: string | null,
): HostApiState {
  if (status && !statusError) {
    return {
      kind: 'live',
      label: 'live',
      tone: 'ok',
      title: `source=/api/status; state=live; last_ok=${formatMaybeDate(statusLoadedAt)}`,
    };
  }

  if (status && statusError) {
    const age = formatAgeSince(statusLoadedAt);
    return {
      kind: 'stale',
      label: age ? `stale ${age}` : 'stale',
      tone: 'warn',
      title: `source=/api/status; state=stale; last_ok=${formatMaybeDate(statusLoadedAt)}; error=${statusError}`,
    };
  }

  if (statusError) {
    return {
      kind: 'offline',
      label: 'offline',
      tone: 'error',
      title: `source=/api/status; state=offline; error=${statusError}`,
    };
  }

  return {
    kind: 'checking',
    label: 'checking',
    tone: 'idle',
    title: 'source=/api/status; state=checking',
  };
}

function deriveRimApiState(status: RimBobStatus | null, host: HostApiState): HeaderState {
  if (host.kind === 'offline' || host.kind === 'checking' || !status) {
    return {
      label: 'unknown',
      tone: 'idle',
      title: `source=/api/status.rimapi_reachable; state=unknown; host_api=${host.kind}`,
    };
  }

  const liveLabel = status.rimapi_reachable ? 'live' : 'waiting';
  if (host.kind === 'stale') {
    return {
      label: `last ${liveLabel}`,
      tone: 'idle',
      title: `source=/api/status.rimapi_reachable; state=stale; last=${status.rimapi_reachable}; host_api=stale`,
    };
  }

  return {
    label: liveLabel,
    tone: status.rimapi_reachable ? 'ok' : 'warn',
    title: `source=/api/status.rimapi_reachable; value=${status.rimapi_reachable}; colony_state_origin=${status.colony_state_origin ?? 'unknown'}`,
  };
}

function deriveLlmState(status: RimBobStatus | null, host: HostApiState): HeaderState {
  if (host.kind === 'offline' || host.kind === 'checking' || !status) {
    return {
      label: 'unknown',
      tone: 'idle',
      title: `source=/api/status.llm_status; state=unknown; host_api=${host.kind}`,
    };
  }

  const llmStatus = status.llm_status ?? (status.llm_configured ? 'ready' : 'missing_key');
  const label = llmLabelFor(llmStatus);
  if (host.kind === 'stale') {
    return {
      label: `last ${label}`,
      tone: 'idle',
      title: llmTitle(status, llmStatus, 'stale'),
    };
  }

  return {
    label,
    tone: llmToneFor(llmStatus),
    title: llmTitle(status, llmStatus, 'live'),
  };
}

function deriveMayorState(status: RimBobStatus | null, host: HostApiState): HeaderState {
  if (host.kind === 'offline' || host.kind === 'checking' || !status) {
    return {
      label: 'unknown',
      tone: 'idle',
      title: `source=/api/status.mayor_running; state=unknown; host_api=${host.kind}`,
    };
  }

  const label = mayorLabel(status);
  if (host.kind === 'stale') {
    return {
      label: `last ${label}`,
      tone: 'idle',
      title: `source=/api/status; state=stale; mayor_running=${status.mayor_running}; mayor_snapshot_version=${status.mayor_snapshot_version ?? 'null'}`,
    };
  }

  return {
    label,
    tone: status.mayor_last_error ? 'error' : status.mayor_running ? 'info' : 'idle',
    title: mayorTitle(status),
  };
}

function deriveStreamState(stream: StreamDiagnostics): HeaderState {
  return {
    label: stream.state,
    tone: stream.state === 'open' ? 'ok' : stream.state === 'error' ? 'warn' : 'idle',
    title: `source=/api/advice/stream; state=${stream.state}; ready_state=${stream.readyState}; events=${stream.eventCount}; reconnects=${stream.reconnectCount}; last_event=${stream.lastEventType ?? 'none'}`,
  };
}

function llmTitle(status: RimBobStatus, llmStatus: string, state: 'live' | 'stale'): string {
  return [
    'source=/api/status',
    `state=${state}`,
    `llm_status=${llmStatus}`,
    `llm_configured=${status.llm_configured}`,
    `llm_last_event_at=${formatMaybeDate(status.llm_last_event_at)}`,
    `llm_last_error=${status.llm_last_error ?? 'null'}`,
  ].join('; ');
}

function llmToneFor(status: string): PillTone {
  if (status === 'missing_key' || status === 'request_failed' || status === 'parse_failed') return 'error';
  if (status === 'ready' || status === 'not_seen_yet') return 'warn';
  if (status === 'parsed' || status === 'normalized') return 'ok';
  return 'warn';
}

function llmLabelFor(status: string): string {
  if (status === 'missing_key') return 'missing key';
  if (status === 'ready') return 'configured';
  return status.replace(/_/g, ' ');
}

function mayorLabel(status: RimBobStatus): string {
  if (status.mayor_last_error) return 'error';
  if (status.mayor_running) return 'running';
  if (status.mayor_snapshot_version !== null) return 'loaded';
  return 'no snapshot';
}

function mayorTitle(status: RimBobStatus): string {
  if (status.mayor_last_error) {
    return `source=/api/status; mayor_last_error=${status.mayor_last_error}`;
  }

  if (status.mayor_running) {
    return `source=/api/status; mayor_running=true; mayor_started_at=${formatMaybeDate(status.mayor_started_at)}`;
  }

  if (status.mayor_snapshot_version !== null) {
    return `source=/api/status; mayor_running=false; mayor_snapshot_version=${status.mayor_snapshot_version}; mayor_completed_at=${formatMaybeDate(status.mayor_completed_at)}`;
  }

  return 'source=/api/status; mayor_running=false; mayor_snapshot_version=null';
}

function formatBuildDateTime(value: string): string {
  if (value === 'unknown') return value;

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  return date.toLocaleString();
}

function dashboardAssetLabel(assetVersion: string): string {
  if (assetVersion === 'missing' || assetVersion === 'unknown') return assetVersion;

  return assetVersion
    .split('|')
    .map(asset => asset.replace(/^index-/, '').replace(/\.(js|css)$/i, ''))
    .join(' / ');
}

function runtimeTooltip(runtimeRoot: string | null, hostProcessPath: string | null): string {
  if (!runtimeRoot && !hostProcessPath) {
    return 'source=/api/system/health; runtime.runtime_root=checking; runtime.host_process_path=checking';
  }

  return [
    'source=/api/system/health',
    `runtime.runtime_root=${runtimeRoot ?? 'unknown'}`,
    `runtime.host_process_path=${hostProcessPath ?? 'unknown'}`,
  ].join('; ');
}

function formatMaybeDate(value: string | null): string {
  if (!value) return 'unknown';
  return formatBuildDateTime(value);
}

function formatAgeSince(value: string | null): string {
  if (!value) return '';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';

  const ageSeconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (ageSeconds < 60) return `${ageSeconds}s`;

  const ageMinutes = Math.round(ageSeconds / 60);
  if (ageMinutes < 60) return `${ageMinutes}m`;

  const ageHours = Math.round(ageMinutes / 60);
  return `${ageHours}h`;
}

function shortPath(value: string, keepSegments: number): string {
  const parts = value.split(/[\\/]+/).filter(Boolean);
  if (parts.length <= keepSegments) return value;

  return `...\\${parts.slice(-keepSegments).join('\\')}`;
}
