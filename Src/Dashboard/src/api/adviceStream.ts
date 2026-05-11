import type { MayorAgenda } from '../types/agenda';
import type { AdviceItem } from '../types/advice';

export type AgendaListener = (agenda: MayorAgenda) => void;
export type AdviceListener = (advice: AdviceItem) => void;
export type StreamErrorListener = (e: Event) => void;

/**
 * Subscribes to /api/advice/stream. Calls onAgenda for each agenda_update event.
 * Returns an unsubscribe function that closes the EventSource.
 */
export function subscribeAgendaUpdates(
  onAgenda: AgendaListener,
  onAdvice?: AdviceListener,
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
  source.addEventListener('advice', evt => {
    if (!onAdvice) return;
    try {
      const data = JSON.parse((evt as MessageEvent).data) as AdviceItem;
      onAdvice(data);
    } catch (err) {
      console.error('Failed to parse advice payload', err);
    }
  });

  if (onError) source.onerror = onError;

  return () => source.close();
}
