import { recordEndpointQueryTiming } from './requestTelemetry';

export async function readJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await timedFetch(url, { signal });
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, url));
  }

  return await response.json() as T;
}

export async function postJson<T>(url: string, signal?: AbortSignal, body?: unknown): Promise<T> {
  const init: RequestInit = { method: 'POST', signal };
  if (body !== undefined) {
    init.headers = { 'Content-Type': 'application/json' };
    init.body = JSON.stringify(body);
  }

  const response = await timedFetch(url, init);
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, url));
  }

  return await response.json() as T;
}

export async function timedFetch(url: string, init: RequestInit = {}): Promise<Response> {
  const startedAt = Date.now();
  const startedAtPerf = performance.now();
  const method = init.method ?? 'GET';

  try {
    const response = await fetch(url, init);
    recordEndpointQueryTiming({
      method,
      url,
      state: response.ok ? 'ok' : 'error',
      statusCode: response.status,
      startedAt,
      durationMs: performance.now() - startedAtPerf,
      error: response.ok ? null : response.statusText || 'Request failed',
    });
    return response;
  } catch (error) {
    const aborted = error instanceof DOMException && error.name === 'AbortError';
    recordEndpointQueryTiming({
      method,
      url,
      state: aborted ? 'aborted' : 'error',
      statusCode: null,
      startedAt,
      durationMs: performance.now() - startedAtPerf,
      error: error instanceof Error ? error.message : String(error),
    });
    throw error;
  }
}

async function readErrorMessage(response: Response, url: string): Promise<string> {
  const detail = await readErrorDetail(response);
  if (detail) return detail;

  return `${url} returned ${response.status}: ${response.statusText || 'Request failed'}`;
}

async function readErrorDetail(response: Response): Promise<string | null> {
  try {
    const body = await response.json() as { title?: string; detail?: string; error?: string; message?: string };
    return body.detail ?? body.error ?? body.message ?? body.title ?? null;
  } catch {
    return null;
  }
}
