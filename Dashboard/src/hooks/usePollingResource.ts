import { useCallback, useEffect, useRef, useState } from 'react';

const MaxBackoffMs = 60_000;
const MaxBackoffExponent = 5;

interface ResourceSnapshot<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  loadedAt: string | null;
  lastErrorAt: string | null;
}

export interface ResourceState<T> extends ResourceSnapshot<T> {
  refresh: () => void;
}

export function usePollingResource<T>(
  loader: (signal: AbortSignal) => Promise<T>,
  intervalMs: number,
): ResourceState<T> {
  const activeController = useRef<AbortController | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);
  const [state, setState] = useState<ResourceSnapshot<T>>({
    data: null,
    error: null,
    loading: true,
    loadedAt: null,
    lastErrorAt: null,
  });

  const refresh = useCallback(() => {
    setRefreshToken(current => current + 1);
  }, []);

  useEffect(() => {
    let cancelled = false;
    let timer: number | null = null;
    let consecutiveFailures = 0;

    function scheduleNextLoad(delayMs: number): void {
      if (cancelled) return;
      timer = window.setTimeout(() => void load(), delayMs);
    }

    function nextDelayMs(): number {
      if (consecutiveFailures === 0) return intervalMs;

      const exponent = Math.min(consecutiveFailures, MaxBackoffExponent);
      return Math.min(MaxBackoffMs, intervalMs * (2 ** exponent));
    }

    async function load(): Promise<void> {
      activeController.current?.abort();
      const controller = new AbortController();
      activeController.current = controller;

      setState(current => ({ ...current, loading: current.data === null }));
      try {
        const data = await loader(controller.signal);
        if (!cancelled) {
          consecutiveFailures = 0;
          setState({
            data,
            error: null,
            loading: false,
            loadedAt: new Date().toISOString(),
            lastErrorAt: null,
          });
        }
      } catch (error) {
        if (!cancelled && !controller.signal.aborted && (error as Error).name !== 'AbortError') {
          consecutiveFailures += 1;
          setState(current => ({
            ...current,
            error: String(error),
            loading: false,
            lastErrorAt: new Date().toISOString(),
          }));
        }
      } finally {
        if (!cancelled) {
          scheduleNextLoad(nextDelayMs());
        }
      }
    }

    void load();

    return () => {
      cancelled = true;
      activeController.current?.abort();
      if (timer !== null) {
        window.clearTimeout(timer);
      }
    };
  }, [loader, intervalMs, refresh, refreshToken]);

  return { ...state, refresh };
}
