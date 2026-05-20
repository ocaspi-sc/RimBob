import { useEffect, useReducer } from 'react';
import { parseAdviceEvent, parseAdviceSnapshotEvent, parseHostReadyEvent } from '../api/adviceStream';
import { fetchLatestAgenda, parseAgendaEvent } from '../api/agenda';
import { recordEndpointQueryTiming } from '../api/requestTelemetry';
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

    const streamUrl = '/api/advice/stream';
    const streamStartedAt = Date.now();
    const streamStartedAtPerf = performance.now();
    const source = new EventSource(streamUrl);
    let streamRecorded = false;
    dispatch({ type: 'streamConnecting', readyState: source.readyState });

    source.onopen = () => {
      if (!streamRecorded) {
        streamRecorded = true;
        recordEndpointQueryTiming({
          method: 'SSE',
          url: streamUrl,
          state: 'stream',
          statusCode: null,
          startedAt: streamStartedAt,
          durationMs: performance.now() - streamStartedAtPerf,
        });
      }
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

    source.addEventListener('host_ready', raw => {
      const event = raw as MessageEvent;
      try {
        const version = parseHostReadyEvent(event);
        dispatch({
          type: 'hostReadyReceived',
          version,
          eventId: event.lastEventId,
          readyState: source.readyState,
        });
      } catch (error) {
        dispatch({ type: 'parseFailed', eventType: 'host_ready', error: String(error), readyState: source.readyState });
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
      if (!streamRecorded) {
        streamRecorded = true;
        recordEndpointQueryTiming({
          method: 'SSE',
          url: streamUrl,
          state: 'error',
          statusCode: null,
          startedAt: streamStartedAt,
          durationMs: performance.now() - streamStartedAtPerf,
          error: 'Advice stream failed to open.',
        });
      }
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
