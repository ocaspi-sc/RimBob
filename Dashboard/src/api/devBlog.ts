import type { DevBlogHistory } from '../types/devBlog';
import { readJson } from './http';

export async function fetchDevBlogHistory(signal?: AbortSignal): Promise<DevBlogHistory> {
  return await readJson<DevBlogHistory>('/api/dev-blog/history', signal);
}
