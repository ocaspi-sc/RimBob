import { useEffect, useState } from 'react';
import type { DependencyList } from 'react';

export interface AsyncState<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
}

export function useAsyncResource<T>(
  loader: (signal: AbortSignal) => Promise<T>,
  deps: DependencyList,
): AsyncState<T> {
  const [state, setState] = useState<AsyncState<T>>({
    data: null,
    error: null,
    loading: true,
  });

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    setState({ data: null, error: null, loading: true });

    loader(controller.signal)
      .then(data => {
        if (!cancelled) setState({ data, error: null, loading: false });
      })
      .catch(error => {
        if (!cancelled && (error as Error).name !== 'AbortError') {
          setState({ data: null, error: String(error), loading: false });
        }
      });

    return () => {
      cancelled = true;
      controller.abort();
    };
  }, deps);

  return state;
}
