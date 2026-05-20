export const SlowEndpointQueryMs = 1000;

export type EndpointQueryState = 'ok' | 'error' | 'aborted' | 'stream';

export interface EndpointQueryTiming {
  id: number;
  method: string;
  url: string;
  state: EndpointQueryState;
  statusCode: number | null;
  startedAt: number;
  completedAt: number;
  durationMs: number;
  error: string | null;
}

interface RecordEndpointQueryTimingInput {
  method: string;
  url: string | URL;
  state: EndpointQueryState;
  statusCode?: number | null;
  startedAt: number;
  durationMs: number;
  error?: string | null;
}

const MaxEndpointTimingEntries = 120;

let nextEndpointTimingId = 1;
let endpointTimings: EndpointQueryTiming[] = [];
const subscribers = new Set<() => void>();

export function recordEndpointQueryTiming(input: RecordEndpointQueryTimingInput): void {
  const completedAt = input.startedAt + input.durationMs;
  endpointTimings = [
    {
      id: nextEndpointTimingId,
      method: input.method.toUpperCase(),
      url: normalizeEndpointUrl(input.url),
      state: input.state,
      statusCode: input.statusCode ?? null,
      startedAt: input.startedAt,
      completedAt,
      durationMs: Math.max(0, input.durationMs),
      error: input.error ?? null,
    },
    ...endpointTimings,
  ].slice(0, MaxEndpointTimingEntries);
  nextEndpointTimingId += 1;
  notifyEndpointTimingSubscribers();
}

export function getEndpointQueryTimings(): EndpointQueryTiming[] {
  return endpointTimings;
}

export function subscribeEndpointQueryTimings(listener: () => void): () => void {
  subscribers.add(listener);
  return () => {
    subscribers.delete(listener);
  };
}

function notifyEndpointTimingSubscribers(): void {
  subscribers.forEach(listener => listener());
}

function normalizeEndpointUrl(url: string | URL): string {
  const raw = String(url);
  try {
    const parsed = new URL(raw, window.location.origin);
    return `${parsed.pathname}${parsed.search}`;
  } catch {
    return raw;
  }
}
