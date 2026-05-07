import type { MayorAgenda } from '../types/agenda';

export type AgendaListener = (agenda: MayorAgenda) => void;
export type StreamErrorListener = (e: Event) => void;

/**
 * Subscribes to /api/advice/stream. Calls onAgenda for each agenda_update event.
 * Returns an unsubscribe function that closes the EventSource.
 */
export function subscribeAgendaUpdates(
  onAgenda: AgendaListener,
  onError?: StreamErrorListener,
): () => void {
  const source = new EventSource('/api/advice/stream');

  source.addEventListener('agenda_update', evt => {
    try {
      const data = JSON.parse((evt as MessageEvent).data) as MayorAgenda;
      onAgenda(data);
    } catch (err) {
      console.error('Failed to parse agenda_update payload', err);
    }
  });

  source.addEventListener('ping', () => { /* keep-alive — nothing to do */ });
  source.addEventListener('advice', () => { /* M3+ feeders — ignore in M1 */ });

  if (onError) source.onerror = onError;

  return () => source.close();
}
