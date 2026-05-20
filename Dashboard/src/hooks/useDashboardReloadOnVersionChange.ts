import { useEffect, useRef } from 'react';
import type { RimBobRunningVersion, SystemHealth } from '../types/system';

const ReloadDelayMs = 350;

export function useDashboardReloadOnVersionChange(
  health: SystemHealth | null,
  streamVersion: RimBobRunningVersion | null,
) {
  const firstSeenToken = useRef<string | null>(null);
  const reloadScheduled = useRef(false);

  useEffect(() => {
    const reloadToken = (streamVersion ?? health?.version ?? null)?.reload_token ?? null;
    if (!reloadToken || reloadScheduled.current) return;

    if (firstSeenToken.current === null) {
      firstSeenToken.current = reloadToken;
      return;
    }

    if (firstSeenToken.current === reloadToken) return;

    reloadScheduled.current = true;
    window.setTimeout(() => window.location.reload(), ReloadDelayMs);
  }, [health, streamVersion]);
}
