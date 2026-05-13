import { useEffect, useState } from 'react';
import type { Dispatch, SetStateAction } from 'react';
import { fetchLatestAgenda, parseAdviceEvent, parseAdviceSnapshotEvent, parseAgendaEvent } from '../api/client';
import type { MayorAgenda } from '../types/agenda';
import type { AdviceItem } from '../types/advice';
import type { DashboardEvent, FeedState, StreamDiagnostics } from '../types/system';

const initialDiagnostics: StreamDiagnostics = {
  state: 'connecting',
  readyState: 0,
  eventCount: 0,
  reconnectCount: 0,
  lastEventType: null,
  lastEventId: null,
  lastEventAt: null,
  lastErrorAt: null,
};

export interface AdviceFeedState {
  agenda: MayorAgenda | null;
  previousAgenda: MayorAgenda | null;
  feed: FeedState;
  stream: StreamDiagnostics;
  events: DashboardEvent[];
  initialAgendaError: string | null;
}

export function useAdviceFeed(): AdviceFeedState {
  const [agenda, setAgenda] = useState<MayorAgenda | null>(null);
  const [previousAgenda, setPreviousAgenda] = useState<MayorAgenda | null>(null);
  const [activeAdvice, setActiveAdvice] = useState<AdviceItem[]>([]);
  const [agendaEvents, setAgendaEvents] = useState(0);
  const [adviceEvents, setAdviceEvents] = useState(0);
  const [stream, setStream] = useState<StreamDiagnostics>(initialDiagnostics);
  const [events, setEvents] = useState<DashboardEvent[]>([]);
  const [initialAgendaError, setInitialAgendaError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    fetchLatestAgenda(controller.signal)
      .then(latest => {
        if (!cancelled) setAgenda(latest);
      })
      .catch(error => {
        if (!cancelled && (error as Error).name !== 'AbortError') {
          setInitialAgendaError(String(error));
          pushEvent(setEvents, 'Agenda', 'fetch_failed', 'warn', String(error));
        }
      });

    const source = new EventSource('/api/advice/stream');
    setStream(current => ({ ...current, state: 'connecting', readyState: source.readyState }));
    pushEvent(setEvents, 'SSE', 'connecting', 'info', 'Connecting to /api/advice/stream');

    source.onopen = () => {
      setStream(current => ({
        ...current,
        state: 'open',
        readyState: source.readyState,
      }));
      pushEvent(setEvents, 'SSE', 'open', 'info', 'Advice stream connected');
    };

    source.addEventListener('agenda_update', raw => {
      const event = raw as MessageEvent;
      try {
        const next = parseAgendaEvent(event);
        setAgenda(current => {
          if (current && current.version === next.version) return current;
          setPreviousAgenda(current);
          return next;
        });
        setAgendaEvents(count => count + 1);
        recordStreamEvent(setStream, source.readyState, 'agenda_update', event.lastEventId);
        pushEvent(setEvents, 'Mayor', 'agenda_update', 'info', `Agenda v${next.version} arrived`);
      } catch (error) {
        recordStreamError(setStream, source.readyState);
        pushEvent(setEvents, 'SSE', 'parse_error', 'error', `Failed to parse agenda_update: ${String(error)}`);
      }
    });

    source.addEventListener('advice', raw => {
      const event = raw as MessageEvent;
      try {
        const advice = parseAdviceEvent(event);
        setActiveAdvice(current => {
          const filtered = current.filter(item => item.id !== advice.id);
          return [advice, ...filtered].sort(compareAdvice);
        });
        setAdviceEvents(count => count + 1);
        recordStreamEvent(setStream, source.readyState, 'advice', event.lastEventId);
        pushEvent(setEvents, advice.minister, 'advice', advice.severity === 'critical' ? 'warn' : 'info', advice.title);
      } catch (error) {
        recordStreamError(setStream, source.readyState);
        pushEvent(setEvents, 'SSE', 'parse_error', 'error', `Failed to parse advice: ${String(error)}`);
      }
    });

    source.addEventListener('advice_snapshot', raw => {
      const event = raw as MessageEvent;
      try {
        const snapshot = parseAdviceSnapshotEvent(event);
        const snapshotMinister = snapshot.minister;
        setActiveAdvice(current => {
          if (!snapshotMinister) return [...snapshot.advice].sort(compareAdvice);

          const filtered = current.filter(item => !sameMinister(item.minister, snapshotMinister));
          return [...snapshot.advice, ...filtered].sort(compareAdvice);
        });
        setAdviceEvents(count => count + 1);
        recordStreamEvent(setStream, source.readyState, 'advice_snapshot', event.lastEventId);
        pushEvent(
          setEvents,
          snapshotMinister ?? 'Advice',
          'advice_snapshot',
          'info',
          `${snapshot.advice.length} active advice item${snapshot.advice.length === 1 ? '' : 's'}`,
        );
      } catch (error) {
        recordStreamError(setStream, source.readyState);
        pushEvent(setEvents, 'SSE', 'parse_error', 'error', `Failed to parse advice_snapshot: ${String(error)}`);
      }
    });

    source.addEventListener('ping', () => {
      recordStreamEvent(setStream, source.readyState, 'ping', null);
    });

    source.onerror = () => {
      setStream(current => ({
        ...current,
        state: 'error',
        readyState: source.readyState,
        reconnectCount: current.reconnectCount + 1,
        lastErrorAt: new Date().toISOString(),
      }));
      pushEvent(setEvents, 'SSE', 'error', 'warn', 'Advice stream reported an error or reconnect');
    };

    return () => {
      cancelled = true;
      controller.abort();
      source.close();
      setStream(current => ({ ...current, state: 'closed', readyState: source.readyState }));
    };
  }, []);

  return {
    agenda,
    previousAgenda,
    feed: {
      agendaEvents,
      adviceEvents,
      activeAdvice,
    },
    stream,
    events,
    initialAgendaError,
  };
}

function compareAdvice(a: AdviceItem, b: AdviceItem): number {
  const severityDelta = severityRank(b.severity) - severityRank(a.severity);
  if (severityDelta !== 0) return severityDelta;
  return b.priority_score - a.priority_score;
}

function severityRank(severity: AdviceItem['severity']): number {
  if (severity === 'critical') return 3;
  if (severity === 'high') return 2;
  if (severity === 'medium') return 1;
  return 0;
}

function sameMinister(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}

function recordStreamEvent(
  setStream: Dispatch<SetStateAction<StreamDiagnostics>>,
  readyState: number,
  eventType: string,
  eventId: string | null,
) {
  setStream(current => ({
    ...current,
    state: 'open',
    readyState,
    eventCount: current.eventCount + 1,
    lastEventType: eventType,
    lastEventId: eventId,
    lastEventAt: new Date().toISOString(),
  }));
}

function recordStreamError(
  setStream: Dispatch<SetStateAction<StreamDiagnostics>>,
  readyState: number,
) {
  setStream(current => ({
    ...current,
    state: 'error',
    readyState,
    lastErrorAt: new Date().toISOString(),
  }));
}

function pushEvent(
  setEvents: Dispatch<SetStateAction<DashboardEvent[]>>,
  source: string,
  type: string,
  severity: DashboardEvent['severity'],
  summary: string,
) {
  const event: DashboardEvent = {
    id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    at: new Date().toISOString(),
    source,
    type,
    severity,
    summary,
  };
  setEvents(current => [event, ...current].slice(0, 80));
}
