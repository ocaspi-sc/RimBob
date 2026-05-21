import { useEffect, useReducer } from 'react';
import { parseAdviceEvent, parseAdviceSnapshotEvent, parseHostReadyEvent } from '../api/adviceStream';
import { fetchLatestAgenda, parseAgendaEvent } from '../api/agenda';
import { recordEndpointQueryTiming } from '../api/requestTelemetry';
import { adviceFeedReducer, initialAdviceFeedState, type AdviceFeedState } from './adviceFeedReducer';

const StreamRetryBaseDelayMs = 2_000;
const StreamRetryMaxDelayMs = 30_000;
const StreamRetryMaxExponent = 4;

export function useAdviceFeed(): AdviceFeedState {
  const [state, dispatch] = useReducer(adviceFeedReducer, initialAdviceFeedState);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    let source: EventSource | null = null;
    let retryTimer: number | null = null;
    let streamRetryAttempt = 0;

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

    function scheduleReconnect(): void {
      if (cancelled) return;

      streamRetryAttempt += 1;
      const retryDelayMs = streamRetryDelayMs(streamRetryAttempt);
      retryTimer = window.setTimeout(() => {
        retryTimer = null;
        connectStream();
      }, retryDelayMs);
    }

    function connectStream(): void {
      if (cancelled) return;

      const streamStartedAt = Date.now();
      const streamStartedAtPerf = performance.now();
      const nextSource = new EventSource(streamUrl);
      let streamRecorded = false;
      let reconnectScheduled = false;

      source = nextSource;
      dispatch({ type: 'streamConnecting', readyState: nextSource.readyState });

      nextSource.onopen = () => {
        streamRetryAttempt = 0;
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
        dispatch({ type: 'streamOpened', readyState: nextSource.readyState });
      };

      nextSource.addEventListener('agenda_update', raw => {
        const event = raw as MessageEvent;
        try {
          const next = parseAgendaEvent(event);
          dispatch({
            type: 'agendaReceived',
            agenda: next,
            eventId: event.lastEventId,
            readyState: nextSource.readyState,
          });
        } catch (error) {
          dispatch({ type: 'parseFailed', eventType: 'agenda_update', error: String(error), readyState: nextSource.readyState });
        }
      });

      nextSource.addEventListener('host_ready', raw => {
        const event = raw as MessageEvent;
        try {
          const version = parseHostReadyEvent(event);
          dispatch({
            type: 'hostReadyReceived',
            version,
            eventId: event.lastEventId,
            readyState: nextSource.readyState,
          });
        } catch (error) {
          dispatch({ type: 'parseFailed', eventType: 'host_ready', error: String(error), readyState: nextSource.readyState });
        }
      });

      nextSource.addEventListener('advice', raw => {
        const event = raw as MessageEvent;
        try {
          const advice = parseAdviceEvent(event);
          dispatch({
            type: 'adviceReceived',
            advice,
            eventId: event.lastEventId,
            readyState: nextSource.readyState,
          });
        } catch (error) {
          dispatch({ type: 'parseFailed', eventType: 'advice', error: String(error), readyState: nextSource.readyState });
        }
      });

      nextSource.addEventListener('advice_snapshot', raw => {
        const event = raw as MessageEvent;
        try {
          const snapshot = parseAdviceSnapshotEvent(event);
          dispatch({
            type: 'adviceSnapshotReceived',
            snapshot,
            eventId: event.lastEventId,
            readyState: nextSource.readyState,
          });
        } catch (error) {
          dispatch({ type: 'parseFailed', eventType: 'advice_snapshot', error: String(error), readyState: nextSource.readyState });
        }
      });

      nextSource.addEventListener('ping', () => {
        dispatch({ type: 'pingReceived', readyState: nextSource.readyState });
      });

      nextSource.onerror = () => {
        if (cancelled || reconnectScheduled) return;

        reconnectScheduled = true;
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
        dispatch({ type: 'streamErrored', readyState: nextSource.readyState });
        nextSource.close();
        if (source === nextSource) {
          source = null;
        }
        scheduleReconnect();
      };
    }

    connectStream();

    return () => {
      cancelled = true;
      controller.abort();
      if (retryTimer !== null) {
        window.clearTimeout(retryTimer);
      }
      if (source !== null) {
        source.close();
        dispatch({ type: 'streamClosed', readyState: source.readyState });
      }
    };
  }, []);

  return state;
}

function streamRetryDelayMs(attempt: number): number {
  const exponent = Math.min(Math.max(0, attempt - 1), StreamRetryMaxExponent);
  return Math.min(StreamRetryMaxDelayMs, StreamRetryBaseDelayMs * (2 ** exponent));
}
