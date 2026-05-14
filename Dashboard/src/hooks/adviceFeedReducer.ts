import type { AdviceItem, AdviceSnapshot } from '../types/advice';
import type { MayorAgenda } from '../types/agenda';
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

export type AdviceFeedAction =
  | { type: 'initialAgendaLoaded'; agenda: MayorAgenda | null }
  | { type: 'initialAgendaFailed'; error: string }
  | { type: 'streamConnecting'; readyState: number }
  | { type: 'streamOpened'; readyState: number }
  | { type: 'agendaReceived'; agenda: MayorAgenda; eventId: string | null; readyState: number }
  | { type: 'adviceReceived'; advice: AdviceItem; eventId: string | null; readyState: number }
  | { type: 'adviceSnapshotReceived'; snapshot: AdviceSnapshot; eventId: string | null; readyState: number }
  | { type: 'pingReceived'; readyState: number }
  | { type: 'parseFailed'; eventType: string; error: string; readyState: number }
  | { type: 'streamErrored'; readyState: number }
  | { type: 'streamClosed'; readyState: number };

export const initialAdviceFeedState: AdviceFeedState = {
  agenda: null,
  previousAgenda: null,
  feed: {
    agendaEvents: 0,
    adviceEvents: 0,
    activeAdvice: [],
  },
  stream: initialDiagnostics,
  events: [],
  initialAgendaError: null,
};

export function adviceFeedReducer(
  state: AdviceFeedState,
  action: AdviceFeedAction,
): AdviceFeedState {
  switch (action.type) {
    case 'initialAgendaLoaded':
      return {
        ...state,
        agenda: action.agenda,
      };
    case 'initialAgendaFailed':
      return {
        ...state,
        initialAgendaError: action.error,
        events: pushEvent(state.events, 'Agenda', 'fetch_failed', 'warn', action.error),
      };
    case 'streamConnecting':
      return {
        ...state,
        stream: {
          ...state.stream,
          state: 'connecting',
          readyState: action.readyState,
        },
        events: pushEvent(state.events, 'SSE', 'connecting', 'info', 'Connecting to /api/advice/stream'),
      };
    case 'streamOpened':
      return {
        ...state,
        stream: {
          ...state.stream,
          state: 'open',
          readyState: action.readyState,
        },
        events: pushEvent(state.events, 'SSE', 'open', 'info', 'Advice stream connected'),
      };
    case 'agendaReceived': {
      const agendaChanged = state.agenda?.version !== action.agenda.version;
      return {
        ...state,
        agenda: agendaChanged ? action.agenda : state.agenda,
        previousAgenda: agendaChanged ? state.agenda : state.previousAgenda,
        feed: {
          ...state.feed,
          agendaEvents: state.feed.agendaEvents + 1,
        },
        stream: recordStreamEvent(state.stream, action.readyState, 'agenda_update', action.eventId),
        events: pushEvent(
          state.events,
          'Mayor',
          'agenda_update',
          'info',
          `Agenda v${action.agenda.version} arrived`,
        ),
      };
    }
    case 'adviceReceived': {
      const filtered = state.feed.activeAdvice.filter(item => item.id !== action.advice.id);
      return {
        ...state,
        feed: {
          ...state.feed,
          adviceEvents: state.feed.adviceEvents + 1,
          activeAdvice: [action.advice, ...filtered].sort(compareAdvice),
        },
        stream: recordStreamEvent(state.stream, action.readyState, 'advice', action.eventId),
        events: pushEvent(
          state.events,
          action.advice.minister,
          'advice',
          action.advice.severity === 'critical' ? 'warn' : 'info',
          action.advice.title,
        ),
      };
    }
    case 'adviceSnapshotReceived': {
      const snapshotMinister = action.snapshot.minister;
      const activeAdvice = snapshotMinister
        ? [
          ...action.snapshot.advice,
          ...state.feed.activeAdvice.filter(item => !sameMinister(item.minister, snapshotMinister)),
        ].sort(compareAdvice)
        : [...action.snapshot.advice].sort(compareAdvice);

      return {
        ...state,
        feed: {
          ...state.feed,
          adviceEvents: state.feed.adviceEvents + 1,
          activeAdvice,
        },
        stream: recordStreamEvent(state.stream, action.readyState, 'advice_snapshot', action.eventId),
        events: pushEvent(
          state.events,
          snapshotMinister ?? 'Advice',
          'advice_snapshot',
          'info',
          `${action.snapshot.advice.length} active advice item${action.snapshot.advice.length === 1 ? '' : 's'}`,
        ),
      };
    }
    case 'pingReceived':
      return {
        ...state,
        stream: recordStreamEvent(state.stream, action.readyState, 'ping', null),
      };
    case 'parseFailed':
      return {
        ...state,
        stream: recordStreamError(state.stream, action.readyState),
        events: pushEvent(
          state.events,
          'SSE',
          'parse_error',
          'error',
          `Failed to parse ${action.eventType}: ${action.error}`,
        ),
      };
    case 'streamErrored':
      return {
        ...state,
        stream: {
          ...state.stream,
          state: 'error',
          readyState: action.readyState,
          reconnectCount: state.stream.reconnectCount + 1,
          lastErrorAt: new Date().toISOString(),
        },
        events: pushEvent(
          state.events,
          'SSE',
          'error',
          'warn',
          'Advice stream reported an error or reconnect',
        ),
      };
    case 'streamClosed':
      return {
        ...state,
        stream: {
          ...state.stream,
          state: 'closed',
          readyState: action.readyState,
        },
      };
  }
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
  stream: StreamDiagnostics,
  readyState: number,
  eventType: string,
  eventId: string | null,
): StreamDiagnostics {
  return {
    ...stream,
    state: 'open',
    readyState,
    eventCount: stream.eventCount + 1,
    lastEventType: eventType,
    lastEventId: eventId,
    lastEventAt: new Date().toISOString(),
  };
}

function recordStreamError(
  stream: StreamDiagnostics,
  readyState: number,
): StreamDiagnostics {
  return {
    ...stream,
    state: 'error',
    readyState,
    lastErrorAt: new Date().toISOString(),
  };
}

function pushEvent(
  events: DashboardEvent[],
  source: string,
  type: string,
  severity: DashboardEvent['severity'],
  summary: string,
): DashboardEvent[] {
  const event: DashboardEvent = {
    id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    at: new Date().toISOString(),
    source,
    type,
    severity,
    summary,
  };

  return [event, ...events].slice(0, 80);
}
