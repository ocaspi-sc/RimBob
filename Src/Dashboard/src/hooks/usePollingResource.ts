import { useEffect, useState } from 'react';

export interface ResourceState<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  loadedAt: string | null;
}

export function usePollingResource<T>(
  loader: (signal: AbortSignal) => Promise<T>,
  intervalMs: number,
): ResourceState<T> {
  const [state, setState] = useState<ResourceState<T>>({
    data: null,
    error: null,
    loading: true,
    loadedAt: null,
  });

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    const load = async () => {
      setState(current => ({ ...current, loading: current.data === null, error: null }));
      try {
        const data = await loader(controller.signal);
        if (!cancelled) {
          setState({
            data,
            error: null,
            loading: false,
            loadedAt: new Date().toISOString(),
          });
        }
      } catch (error) {
        if (!cancelled && (error as Error).name !== 'AbortError') {
          setState(current => ({
            ...current,
            error: String(error),
            loading: false,
          }));
        }
      }
    };

    void load();
    const timer = window.setInterval(() => void load(), intervalMs);

    return () => {
      cancelled = true;
      controller.abort();
      window.clearInterval(timer);
    };
  }, [loader, intervalMs]);

  return state;
}
