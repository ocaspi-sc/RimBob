import type { AdviceChainModel, AdviceItem, AdviceSnapshot, AgentFlag } from '../types/advice';
import type { MayorAgenda } from '../types/agenda';
import type { CabinetRunLogSnapshot, DashboardEvent, FeedState, RimBobRunningVersion, StreamDiagnostics } from '../types/system';

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
  cabinetRuns: CabinetRunLogSnapshot[];
  runningVersion: RimBobRunningVersion | null;
  stream: StreamDiagnostics;
  events: DashboardEvent[];
  initialAgendaError: string | null;
}

export type AdviceFeedAction =
  | { type: 'initialAgendaLoaded'; agenda: MayorAgenda | null }
  | { type: 'initialAgendaFailed'; error: string }
  | { type: 'streamConnecting'; readyState: number }
  | { type: 'streamOpened'; readyState: number }
  | { type: 'hostReadyReceived'; version: RimBobRunningVersion; eventId: string | null; readyState: number }
  | { type: 'agendaReceived'; agenda: MayorAgenda; eventId: string | null; readyState: number }
  | { type: 'adviceReceived'; advice: AdviceItem; eventId: string | null; readyState: number }
  | { type: 'adviceSnapshotReceived'; snapshot: AdviceSnapshot; eventId: string | null; readyState: number }
  | { type: 'cabinetRunReceived'; run: CabinetRunLogSnapshot; eventId: string | null; readyState: number }
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
    chains: {},
    flags: {},
    stateSummaries: {},
  },
  cabinetRuns: [],
  runningVersion: null,
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
    case 'hostReadyReceived':
      return {
        ...state,
        runningVersion: action.version,
        stream: recordStreamEvent(state.stream, action.readyState, 'host_ready', action.eventId),
        events: pushEvent(
          state.events,
          'Host',
          'host_ready',
          'info',
          `Host ready ${versionSummary(action.version)}`,
        ),
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
          action.advice.priority === 'critical' ? 'warn' : 'info',
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
      const stateSummaries = mergeStateSummaries(
        state.feed.stateSummaries,
        snapshotMinister,
        action.snapshot.state_summary,
        action.snapshot.state_summaries,
      );
      const chains = mergeChains(
        state.feed.chains,
        snapshotMinister,
        action.snapshot.chain,
        action.snapshot.chains,
      );
      const flags = mergeFlags(
        state.feed.flags,
        snapshotMinister,
        action.snapshot.flags,
      );

      return {
        ...state,
        feed: {
          ...state.feed,
          adviceEvents: state.feed.adviceEvents + 1,
          activeAdvice,
          chains,
          flags,
          stateSummaries,
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
    case 'cabinetRunReceived':
      return {
        ...state,
        cabinetRuns: mergeCabinetRuns(state.cabinetRuns, action.run),
        stream: recordStreamEvent(state.stream, action.readyState, 'cabinet_run', action.eventId),
        events: pushEvent(
          state.events,
          'Cabinet',
          'cabinet_run',
          action.run.status === 'failed' ? 'error' : 'info',
          `Cabinet run ${action.run.status}: ${action.run.steps.length} step${action.run.steps.length === 1 ? '' : 's'}`,
        ),
      };
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
  return priorityRank(b.priority) - priorityRank(a.priority);
}

function priorityRank(priority: AdviceItem['priority']): number {
  if (priority === 'critical') return 3;
  if (priority === 'high') return 2;
  if (priority === 'medium') return 1;
  return 0;
}

function sameMinister(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;
}

function mergeCabinetRuns(
  current: CabinetRunLogSnapshot[],
  nextRun: CabinetRunLogSnapshot,
): CabinetRunLogSnapshot[] {
  return [
    nextRun,
    ...current.filter(run => run.run_id !== nextRun.run_id),
  ].slice(0, 12);
}

function mergeStateSummaries(
  current: Record<string, string>,
  snapshotMinister: string | null | undefined,
  stateSummary: string | null | undefined,
  stateSummaries: Record<string, string> | null | undefined,
): Record<string, string> {
  if (!snapshotMinister) {
    return stateSummaries ? { ...stateSummaries } : current;
  }

  const next = { ...current };
  if (stateSummary && stateSummary.trim().length > 0) {
    next[snapshotMinister] = stateSummary.trim();
  } else {
    delete next[snapshotMinister];
  }
  return next;
}

function mergeChains(
  current: Record<string, AdviceChainModel>,
  snapshotMinister: string | null | undefined,
  chain: AdviceChainModel | null | undefined,
  chains: Record<string, AdviceChainModel> | null | undefined,
): Record<string, AdviceChainModel> {
  if (!snapshotMinister) {
    return chains ? { ...chains } : current;
  }

  const next = { ...current };
  if (chain) {
    next[snapshotMinister] = chain;
  } else {
    delete next[snapshotMinister];
  }
  return next;
}

function mergeFlags(
  current: Record<string, AgentFlag[]>,
  snapshotMinister: string | null | undefined,
  flags: AgentFlag[] | null | undefined,
): Record<string, AgentFlag[]> {
  if (!snapshotMinister) {
    if (!flags) return current;

    return flags.reduce<Record<string, AgentFlag[]>>((next, flag) => {
      const minister = flag.source_minister;
      next[minister] = [...(next[minister] ?? []), flag];
      return next;
    }, {});
  }

  const next = { ...current };
  if (flags && flags.length > 0) {
    next[snapshotMinister] = [...flags];
  } else {
    delete next[snapshotMinister];
  }
  return next;
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

function versionSummary(version: RimBobRunningVersion): string {
  const revision = version.build_revision_short ?? version.build_version;
  return `RimBob ${version.running_version} ${revision}`;
}
