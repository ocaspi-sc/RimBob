export async function readJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(`${url} returned ${response.status}`);
  }

  return await response.json() as T;
}

export async function postJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, { method: 'POST', signal });
  if (!response.ok) {
    throw new Error(`${url} returned ${response.status}: ${await readErrorDetail(response)}`);
  }

  return await response.json() as T;
}

async function readErrorDetail(response: Response): Promise<string> {
  try {
    const body = await response.json() as { title?: string; detail?: string; error?: string };
    return body.detail ?? body.error ?? body.title ?? response.statusText;
  } catch {
    return response.statusText;
  }
}
