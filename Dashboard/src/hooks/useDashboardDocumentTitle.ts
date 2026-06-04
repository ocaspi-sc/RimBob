import { useEffect, useMemo, useState } from 'react';
import type { RimBobRunningVersion, SystemHealth } from '../types/system';

const ClockRefreshMs = 15_000;
const UnknownValue = 'unknown';

export function useDashboardDocumentTitle(
  health: SystemHealth | null,
  streamVersion: RimBobRunningVersion | null,
) {
  const [clockLabel, setClockLabel] = useState(() => formatClock(new Date()));

  useEffect(() => {
    const timer = window.setInterval(() => setClockLabel(formatClock(new Date())), ClockRefreshMs);
    return () => window.clearInterval(timer);
  }, []);

  const title = useMemo(
    () => buildDashboardTitle(streamVersion ?? health?.version ?? null, clockLabel, window.location),
    [clockLabel, health?.version, streamVersion],
  );

  useEffect(() => {
    document.title = title;
  }, [title]);
}

function buildDashboardTitle(
  version: RimBobRunningVersion | null,
  clockLabel: string,
  location: Location,
): string {
  return [
    `RimBob ${formatPort(location)}`,
    formatVersion(version),
    clockLabel,
  ].join(' | ');
}

function formatPort(location: Location): string {
  if (location.port) return `:${location.port}`;
  if (location.protocol === 'http:') return ':80';
  if (location.protocol === 'https:') return ':443';
  if (location.protocol === 'file:') return 'file';
  return location.host || location.protocol.replace(':', '') || UnknownValue;
}

function formatVersion(version: RimBobRunningVersion | null): string {
  if (!version) return 'version checking';

  const runningVersion = cleanVersionPart(version.running_version);
  const revision = cleanVersionPart(version.build_revision_short);
  const buildVersion = cleanVersionPart(version.build_version);
  const primary = runningVersion ? prefixVersion(runningVersion) : 'version unknown';

  if (revision) return `${primary} @${revision}`;
  if (buildVersion) return `${primary} build ${buildVersion}`;
  return primary;
}

function cleanVersionPart(value: string | null): string | null {
  if (!value) return null;
  const trimmed = value.trim();
  if (!trimmed || trimmed.toLowerCase() === UnknownValue) return null;
  return trimmed;
}

function prefixVersion(value: string): string {
  return value.toLowerCase().startsWith('v') ? value : `v${value}`;
}

function formatClock(value: Date): string {
  const hours = value.getHours().toString().padStart(2, '0');
  const minutes = value.getMinutes().toString().padStart(2, '0');
  return `${hours}:${minutes}`;
}
