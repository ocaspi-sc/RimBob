import { useEffect, useReducer } from 'react';
import { parseAdviceEvent, parseAdviceSnapshotEvent } from '../api/adviceStream';
import { fetchLatestAgenda, parseAgendaEvent } from '../api/agenda';
import { adviceFeedReducer, initialAdviceFeedState, type AdviceFeedState } from './adviceFeedReducer';

export function useAdviceFeed(): AdviceFeedState {
  const [state, dispatch] = useReducer(adviceFeedReducer, initialAdviceFeedState);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    fetchLatestAgenda(controller.signal)
      .then(latest => {
        if (!cancelled) {
          dispatch({ type: 'initialAgendaLoaded', agenda: latest });
        }
      })
      .catch(error => {
        if (!cancelled && (error as Error).name !== 'AbortError') {
          dispatch({ type: 'initialAgendaFailed', error: String(error) });
        }
      });

    const source = new EventSource('/api/advice/stream');
    dispatch({ type: 'streamConnecting', readyState: source.readyState });

    source.onopen = () => {
      dispatch({ type: 'streamOpened', readyState: source.readyState });
    };

    source.addEventListener('agenda_update', raw => {
      const event = raw as MessageEvent;
      try {
        const next = parseAgendaEvent(event);
        dispatch({
          type: 'agendaReceived',
          agenda: next,
          eventId: event.lastEventId,
          readyState: source.readyState,
        });
      } catch (error) {
        dispatch({ type: 'parseFailed', eventType: 'agenda_update', error: String(error), readyState: source.readyState });
      }
    });

    source.addEventListener('advice', raw => {
      const event = raw as MessageEvent;
      try {
        const advice = parseAdviceEvent(event);
        dispatch({
          type: 'adviceReceived',
          advice,
          eventId: event.lastEventId,
          readyState: source.readyState,
        });
      } catch (error) {
        dispatch({ type: 'parseFailed', eventType: 'advice', error: String(error), readyState: source.readyState });
      }
    });

    source.addEventListener('advice_snapshot', raw => {
      const event = raw as MessageEvent;
      try {
        const snapshot = parseAdviceSnapshotEvent(event);
        dispatch({
          type: 'adviceSnapshotReceived',
          snapshot,
          eventId: event.lastEventId,
          readyState: source.readyState,
        });
      } catch (error) {
        dispatch({ type: 'parseFailed', eventType: 'advice_snapshot', error: String(error), readyState: source.readyState });
      }
    });

    source.addEventListener('ping', () => {
      dispatch({ type: 'pingReceived', readyState: source.readyState });
    });

    source.onerror = () => {
      dispatch({ type: 'streamErrored', readyState: source.readyState });
    };

    return () => {
      cancelled = true;
      controller.abort();
      source.close();
      dispatch({ type: 'streamClosed', readyState: source.readyState });
    };
  }, []);

  return state;
}
