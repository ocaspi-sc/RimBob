import { useCallback, useEffect, useRef, useState } from 'react';

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

    const load = async () => {
      activeController.current?.abort();
      const controller = new AbortController();
      activeController.current = controller;

      setState(current => ({ ...current, loading: current.data === null }));
      try {
        const data = await loader(controller.signal);
        if (!cancelled) {
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
          setState(current => ({
            ...current,
            error: String(error),
            loading: false,
            lastErrorAt: new Date().toISOString(),
          }));
        }
      }
    };

    void load();
    const timer = window.setInterval(() => void load(), intervalMs);

    return () => {
      cancelled = true;
      activeController.current?.abort();
      window.clearInterval(timer);
    };
  }, [loader, intervalMs, refresh, refreshToken]);

  return { ...state, refresh };
}
