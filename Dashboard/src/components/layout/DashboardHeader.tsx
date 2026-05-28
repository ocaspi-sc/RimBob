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
  const rimWorldState = deriveRimWorldState(status, hostState);
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
        <div className="title-status-row">
          <h1>RimBob Dashboard v2</h1>
          <div className="running-version" aria-label="Running RimBob version">
            <span title={version ? `Running Host version: ${version.running_version}.` : 'Waiting for the running Host version.'}>
              {versionMarker}
            </span>
            <code title={version ? `Running Host build revision: ${version.build_revision ?? version.build_version}.` : 'Waiting for the running Host build revision.'}>
              {revisionMarker}
            </code>
            <span title={version ? `Host build time: ${formatBuildDateTime(version.build_datetime)}.` : 'Waiting for the Host build time.'}>
              {buildMarker}
            </span>
            <span title={version ? dashboardAssetTooltip(version.dashboard_asset_version) : 'Waiting for the dashboard bundle fingerprint.'}>
              {assetMarker}
            </span>
            <span title={runtimeTooltip(runtimeRoot, hostProcessPath)}>
              {rootMarker}
            </span>
          </div>
          <div className="header-status">
            <StatusPill tone={hostState.tone} title={hostState.title}>Host API {hostState.label}</StatusPill>
            <StatusPill tone={rimWorldState.tone} title={rimWorldState.title}>RimWorld {rimWorldState.label}</StatusPill>
            <StatusPill tone={rimApiState.tone} title={rimApiState.title}>RIMAPI {rimApiState.label}</StatusPill>
            <StatusPill tone={llmState.tone} title={llmState.title}>LLM {llmState.label}</StatusPill>
            <StatusPill tone={streamState.tone} title={streamState.title}>SSE {streamState.label}</StatusPill>
            <StatusPill tone={mayorState.tone} title={mayorState.title}>Mayor {mayorState.label}</StatusPill>
          </div>
        </div>
      </div>
      <div className="header-controls">
        <button
          type="button"
          className="trigger-button global"
          disabled={triggerDisabled}
          aria-busy={triggerPending}
          onClick={onTriggerCabinet}
          title="Run all currently wired live ministers now."
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
      title: `Host API is live. Last successful status poll: ${formatMaybeDate(statusLoadedAt)}.`,
    };
  }

  if (status && statusError) {
    const age = formatAgeSince(statusLoadedAt);
    return {
      kind: 'stale',
      label: age ? `stale ${age}` : 'stale',
      tone: 'warn',
      title: `Host API is stale. Last successful poll: ${formatMaybeDate(statusLoadedAt)}. Current error: ${statusError}`,
    };
  }

  if (statusError) {
    return {
      kind: 'offline',
      label: 'offline',
      tone: 'error',
      title: `Host API is offline. Current error: ${statusError}`,
    };
  }

  return {
    kind: 'checking',
    label: 'checking',
    tone: 'idle',
    title: 'Host API is checking. Waiting for the first successful status poll.',
  };
}

function deriveRimApiState(status: RimBobStatus | null, host: HostApiState): HeaderState {
  if (host.kind === 'offline' || host.kind === 'checking' || !status) {
    return {
      label: 'unknown',
      tone: 'idle',
      title: `RIMAPI status is unknown. Host API is ${host.kind}.`,
    };
  }

  const label = status.rimapi_reachable ? 'reachable' : 'offline';
  if (host.kind === 'stale') {
    return {
      label: `last ${label}`,
      tone: 'idle',
      title: `RIMAPI was last known ${label}. Host API is stale, so this is not current. Latest colony refresh: ${formatMaybeDate(status.last_live_refresh_at ?? null)}. Last colony state origin: ${colonyStateOrigin(status)}.`,
    };
  }

  return {
    label,
    tone: status.rimapi_reachable ? 'ok' : 'error',
    title: status.rimapi_reachable
      ? `RIMAPI is reachable. Host can read the loaded mod. Latest colony refresh: ${formatMaybeDate(status.last_live_refresh_at ?? null)}. Colony state origin: ${colonyStateOrigin(status)}.`
      : `RIMAPI is offline. Host cannot read the loaded mod right now. Latest colony refresh: ${formatMaybeDate(status.last_live_refresh_at ?? null)}. Last error: ${rimApiError(status)}.`,
  };
}

function deriveRimWorldState(status: RimBobStatus | null, host: HostApiState): HeaderState {
  if (host.kind === 'offline' || host.kind === 'checking' || !status) {
    return {
      label: 'unknown',
      tone: 'idle',
      title: `RimWorld status is unknown. Host API is ${host.kind}.`,
    };
  }

  const label = rimWorldLabel(status);
  if (host.kind === 'stale') {
    return {
      label: `last ${label}`,
      tone: 'idle',
      title: `RimWorld was last known ${label}. Host API is stale, so this is not current. ${rimWorldRuntimeDetails(status)}`,
    };
  }

  if (!status.rimapi_reachable) {
    return {
      label,
      tone: 'idle',
      title: `RimWorld status is unknown because RIMAPI is offline. ${rimWorldRuntimeDetails(status)}`,
    };
  }

  return {
    label,
    tone: status.rimworld?.live ? 'ok' : 'warn',
    title: status.rimworld?.live
      ? `RimWorld is live. A colony map is loaded and the mod reports game state. ${rimWorldRuntimeDetails(status)}`
      : `RimWorld is waiting for a loaded colony map. RIMAPI is reachable, but live colony play is not confirmed. ${rimWorldRuntimeDetails(status)}`,
  };
}

function deriveLlmState(status: RimBobStatus | null, host: HostApiState): HeaderState {
  if (host.kind === 'offline' || host.kind === 'checking' || !status) {
    return {
      label: 'unknown',
      tone: 'idle',
      title: `LLM status is unknown. Host API is ${host.kind}.`,
    };
  }

  const llmStatus = status.llm_status ?? (status.llm_configured ? 'ready' : 'missing_key');
  const label = llmLabelFor(llmStatus);
  if (host.kind === 'stale') {
    return {
      label: `last ${label}`,
      tone: 'idle',
      title: llmTitle(status, llmStatus, 'stale', label),
    };
  }

  return {
    label,
    tone: llmToneFor(llmStatus),
    title: llmTitle(status, llmStatus, 'live', label),
  };
}

function deriveMayorState(status: RimBobStatus | null, host: HostApiState): HeaderState {
  if (host.kind === 'offline' || host.kind === 'checking' || !status) {
    return {
      label: 'unknown',
      tone: 'idle',
      title: `Mayor status is unknown. Host API is ${host.kind}.`,
    };
  }

  const label = mayorLabel(status);
  if (host.kind === 'stale') {
    return {
      label: `last ${label}`,
      tone: 'idle',
      title: `Mayor was last known ${label}. Snapshot version: ${status.mayor_snapshot_version ?? 'none'}. Host API is stale, so this is not current.`,
    };
  }

  return {
    label,
    tone: mayorTone(status),
    title: mayorTitle(status),
  };
}

function deriveStreamState(stream: StreamDiagnostics): HeaderState {
  return {
    label: stream.state,
    tone: stream.state === 'open' ? 'ok' : stream.state === 'error' ? 'warn' : 'idle',
    title: `Server-Sent Events advice stream is ${stream.state}. Live advice events: ${stream.eventCount}. Reconnects: ${stream.reconnectCount}. Last event: ${stream.lastEventType ?? 'none'}.`,
  };
}

function colonyStateOrigin(status: RimBobStatus): string {
  return status.colony_state_origin ?? 'unknown';
}

function rimApiError(status: RimBobStatus): string {
  if (!status.rimapi_last_error) return 'none';
  return status.rimapi_last_error.replace(/https?:\/\/\S+/g, 'local endpoint').replace(/\s+/g, ' ').slice(0, 160);
}

function rimWorldLabel(status: RimBobStatus): string {
  if (!status.rimapi_reachable) return 'unknown';
  if (status.rimworld?.live) return 'live';
  return 'waiting';
}

function rimWorldRuntimeDetails(status: RimBobStatus): string {
  const runtime = status.rimworld;
  return [
    `Program state: ${runtime?.program_state ?? 'unknown'}.`,
    `Maps: ${runtime?.map_count ?? 'unknown'}.`,
    `Colonists: ${runtime?.colonist_count ?? 'unknown'}.`,
    `Game tick: ${runtime?.game_tick?.toLocaleString() ?? 'unknown'}.`,
    `Paused: ${runtime?.is_paused === null || runtime?.is_paused === undefined ? 'unknown' : runtime.is_paused ? 'yes' : 'no'}.`,
  ].join(' ');
}

function llmTitle(status: RimBobStatus, llmStatus: string, state: 'live' | 'stale', label: string): string {
  const prefix = state === 'stale'
    ? `LLM was last known ${label}. Host API is stale, so this is not current.`
    : `LLM is ${label}.`;
  const explanation = llmStatus === 'ready' || llmStatus === 'not_seen_yet'
    ? 'Gemini is configured, but no provider result has been recorded yet.'
    : llmStatus === 'missing_key'
      ? 'Gemini key is missing, so LLM calls need configuration before they can run.'
      : llmStatus === 'request_failed'
        ? 'The latest LLM request failed.'
        : llmStatus === 'parse_failed'
          ? 'The latest LLM response could not be parsed.'
          : llmStatus === 'parsed' || llmStatus === 'normalized'
            ? 'The latest LLM provider result was received and processed.'
            : 'The latest LLM status is not recognized by the dashboard.';

  return `${prefix} ${explanation} Key configured: ${status.llm_configured ? 'yes' : 'no'}. Last event: ${formatMaybeDate(status.llm_last_event_at)}. Last error: ${status.llm_last_error ?? 'none'}.`;
}

function llmToneFor(status: string): PillTone {
  if (status === 'request_failed' || status === 'parse_failed') return 'error';
  if (status === 'missing_key') return 'warn';
  if (status === 'ready' || status === 'not_seen_yet') return 'info';
  if (status === 'parsed' || status === 'normalized') return 'ok';
  return 'warn';
}

function llmLabelFor(status: string): string {
  if (status === 'missing_key') return 'missing key';
  if (status === 'ready') return 'configured';
  if (status === 'not_seen_yet') return 'no result';
  if (status === 'request_failed') return 'request failed';
  if (status === 'parse_failed') return 'parse failed';
  return status.replace(/_/g, ' ');
}

function mayorLabel(status: RimBobStatus): string {
  if (status.mayor_last_error) return 'error';
  if (status.mayor_running) return 'running';
  if (status.mayor_snapshot_version !== null) return 'loaded';
  return 'no snapshot';
}

function mayorTone(status: RimBobStatus): PillTone {
  if (status.mayor_last_error) return 'error';
  if (status.mayor_running || status.mayor_snapshot_version !== null) return 'info';
  return 'idle';
}

function mayorTitle(status: RimBobStatus): string {
  if (status.mayor_last_error) {
    return `Mayor last error: ${status.mayor_last_error}`;
  }

  if (status.mayor_running) {
    return `Mayor is running. Started: ${formatMaybeDate(status.mayor_started_at)}.`;
  }

  if (status.mayor_snapshot_version !== null) {
    return `Mayor snapshot is loaded. No Mayor run is active. Snapshot version: ${status.mayor_snapshot_version}. Last completed: ${formatMaybeDate(status.mayor_completed_at)}.`;
  }

  return 'No Mayor snapshot is loaded, and no Mayor run is active.';
}

function formatBuildDateTime(value: string): string {
  if (value === 'unknown') return value;

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  return date.toLocaleString();
}

function dashboardAssetLabel(assetVersion: string): string {
  if (assetVersion === 'missing' || assetVersion === 'unknown') return assetVersion;

  return 'loaded';
}

function dashboardAssetTooltip(assetVersion: string): string {
  if (assetVersion === 'missing') return 'Dashboard bundle fingerprint is missing.';
  if (assetVersion === 'unknown') return 'Dashboard bundle fingerprint is unknown.';

  const assets = assetVersion.split('|');
  const jsAsset = assets.find(asset => asset.endsWith('.js')) ?? 'unknown';
  const cssAsset = assets.find(asset => asset.endsWith('.css')) ?? 'unknown';
  return `Dashboard UI bundle loaded. JS fingerprint: ${jsAsset}. CSS fingerprint: ${cssAsset}.`;
}

function runtimeTooltip(runtimeRoot: string | null, hostProcessPath: string | null): string {
  if (!runtimeRoot && !hostProcessPath) {
    return 'Waiting for Host runtime path metadata.';
  }

  return [
    `Dashboard is served from: ${runtimeRoot ?? 'unknown'}.`,
    `Host process: ${hostProcessPath ?? 'unknown'}.`,
  ].join(' ');
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
