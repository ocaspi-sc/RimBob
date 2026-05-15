export async function readJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, url));
  }

  return await response.json() as T;
}

export async function postJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { method: 'POST', signal });
  if (!response.ok) {
    throw new Error(await readErrorMessage(response, url));
  }

  return await response.json() as T;
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
