import {
  deriveHostApiState,
  deriveLlmState,
  deriveMayorState,
  deriveRimApiState,
  deriveRimWorldState,
  deriveStreamState,
} from '../../dashboard/connectivityStatus';
import { displayMinisterName } from '../../dashboard/scopes';
import type { RimBobStatus } from '../../types/status';
import type { RimBobRunningVersion, StreamDiagnostics } from '../../types/system';
import { StatusPill } from '../shared/StatusPill';

const MayorDisplayName = displayMinisterName('Mayor');

export function DashboardHeader({
  hostProcessPath,
  runtimeRoot,
  status,
  statusError,
  statusLoadedAt,
  stream,
  version,
}: {
  hostProcessPath: string | null;
  runtimeRoot: string | null;
  status: RimBobStatus | null;
  statusError: string | null;
  statusLoadedAt: string | null;
  stream: StreamDiagnostics;
  version: RimBobRunningVersion | null;
}) {
  const hostState = deriveHostApiState(status, statusError, statusLoadedAt);
  const rimWorldState = deriveRimWorldState(status, hostState);
  const rimApiState = deriveRimApiState(status, hostState);
  const llmState = deriveLlmState(status, hostState);
  const mayorState = deriveMayorState(status, hostState);
  const streamState = deriveStreamState(stream);
  const versionMarker = version ? `RimBob ${version.running_version}` : 'RimBob checking';
  const commitMarker = version ? `commit ${version.build_revision_short ?? 'unknown'}` : 'commit checking';
  const buildMarker = version ? `built ${formatBuildDateTime(version.build_datetime)}` : 'built checking';
  const rootMarker = runtimeRoot ? `root ${shortPath(runtimeRoot, 3)}` : 'root checking';

  return (
    <header className="dashboard-header panel-shell">
      <div className="brand-block">
        <div className="title-status-row">
          <h1>RimBob Dashboard v2</h1>
          <div className="running-version" aria-label="Running RimBob version">
            <span title={version ? `Running Host version: ${version.running_version}.` : 'Waiting for the running Host version.'}>
              {versionMarker}
            </span>
            <span className="version-commit-chip" title={version ? buildRevisionTooltip(version) : 'Waiting for the Host build revision.'}>
              {commitMarker}
            </span>
            <span title={version ? `Host build time: ${formatBuildDateTime(version.build_datetime)}.` : 'Waiting for the Host build time.'}>
              {buildMarker}
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
            <StatusPill tone={mayorState.tone} title={mayorState.title}>{MayorDisplayName} {mayorState.label}</StatusPill>
          </div>
        </div>
      </div>
    </header>
  );
}

function formatBuildDateTime(value: string): string {
  if (value === 'unknown') return value;

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  return date.toLocaleString();
}

function buildRevisionTooltip(version: RimBobRunningVersion): string {
  if (!version.build_revision) {
    return `Host build revision is not exposed. Informational version: ${version.build_informational_version}.`;
  }

  return `Host was built from Git commit ${version.build_revision}. Informational version: ${version.build_informational_version}.`;
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

function shortPath(value: string, keepSegments: number): string {
  const parts = value.split(/[\\/]+/).filter(Boolean);
  if (parts.length <= keepSegments) return value;

  return `...\\${parts.slice(-keepSegments).join('\\')}`;
}
