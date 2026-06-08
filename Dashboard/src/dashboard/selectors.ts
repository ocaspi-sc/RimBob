import type { DashboardViewKey, ScopeConfig, ScopeKey } from './scopes';
import type { AdviceItem, AgentFlag } from '../types/advice';
import type { AssistedApplyAttempt, CabinetRunLogSnapshot, CabinetRunStepSnapshot, MinisterTrace } from '../types/system';
import type { SystemHealth } from '../types/system';

const LogEntryLimit = 40;
const CabinetRunTraceMatchSlackMs = 2_000;

export type LogEntryKind = 'action_applied' | 'cabinet_run' | 'minister_run' | 'escalation' | 'critical_advice';
export type LogTone = 'ok' | 'info' | 'warn' | 'error';

export interface LogChip {
  emoji: string;
  label: string;
  tooltip: string;
}

export interface LogLink {
  ariaLabel: string;
  href: string;
  tooltip: string;
}

export interface LogEntry {
  id: string;
  at: string;
  kind: LogEntryKind;
  tone: LogTone;
  title: string;
  icon: string;
  chips: LogChip[];
  link?: LogLink;
  steps?: LogStep[];
  tooltip: string;
}

export interface LogStep {
  advice: LogNamedItem[];
  adviceCount: number | null;
  id: string;
  at: string;
  children: LogStep[];
  detail: string | null;
  durationMs: number | null;
  flags: LogNamedItem[];
  flagCount: number | null;
  kind: string;
  label: string;
  link?: LogLink;
  optionsCount: number | null;
  ruleNames: string[];
  status: string;
  tone: LogTone;
}

export interface LogNamedItem {
  label: string;
  tooltip: string;
}

export function deriveLogEntries(input: {
  activeAdvice: AdviceItem[];
  applyAttempts: AssistedApplyAttempt[];
  cabinetRuns: CabinetRunLogSnapshot[];
  flags: Record<string, AgentFlag[]>;
  traces: MinisterTrace[];
  criticalAdvice: AdviceItem[];
}): LogEntry[] {
  const visibleTraces = input.traces.filter(trace => !isManualMinisterTraceCoveredByCabinetRun(trace, input.cabinetRuns));
  const entries = [
    ...input.applyAttempts.map(logEntryForApplyAttempt),
    ...input.cabinetRuns.map(run => logEntryForCabinetRun(run, input.activeAdvice, input.flags)),
    ...visibleTraces.map(logEntryForTrace).filter((entry): entry is LogEntry => entry !== null),
    ...input.criticalAdvice.map(logEntryForCriticalAdvice),
  ];

  const uniqueEntries = new Map<string, LogEntry>();
  for (const entry of entries) {
    if (!uniqueEntries.has(entry.id)) {
      uniqueEntries.set(entry.id, entry);
    }
  }

  return [...uniqueEntries.values()]
    .sort((left, right) => timestampOf(right.at) - timestampOf(left.at))
    .slice(0, LogEntryLimit);
}

function isManualMinisterTraceCoveredByCabinetRun(
  trace: MinisterTrace,
  cabinetRuns: CabinetRunLogSnapshot[],
): boolean {
  if (normalizeReference(trace.trigger) !== 'manualtrigger') return false;
  if (trace.escalationReason || trace.path === 'llm' || trace.path === 'llm_failed') return false;

  const traceMinister = normalizedMinisterScope(trace.minister);
  if (!traceMinister) return false;

  const traceStartedAt = timestampOf(trace.startedAt);
  const traceCompletedAt = timestampOf(trace.completedAt ?? trace.startedAt);
  if (traceStartedAt === 0 && traceCompletedAt === 0) return false;

  return cabinetRuns.some(run =>
    flattenCabinetSteps(run.steps).some(step =>
      cabinetStepCoversTrace(step, traceMinister, trace.path, traceStartedAt, traceCompletedAt),
    ),
  );
}

function cabinetStepCoversTrace(
  step: CabinetRunStepSnapshot,
  traceMinister: string,
  tracePath: string,
  traceStartedAt: number,
  traceCompletedAt: number,
): boolean {
  if (normalizedMinisterScope(step.minister ?? '') !== traceMinister) return false;

  if (step.trace_path && normalizeReference(step.trace_path) !== normalizeReference(tracePath)) {
    return false;
  }

  const stepStartedAt = timestampOf(step.started_at);
  const stepCompletedAt = timestampOf(step.completed_at ?? step.started_at);
  if (stepStartedAt === 0 && stepCompletedAt === 0) return false;

  return rangesOverlap(
    traceStartedAt,
    traceCompletedAt,
    stepStartedAt,
    stepCompletedAt,
    CabinetRunTraceMatchSlackMs,
  );
}

function flattenCabinetSteps(steps: CabinetRunStepSnapshot[]): CabinetRunStepSnapshot[] {
  return steps.flatMap(step => [step, ...flattenCabinetSteps(step.children ?? [])]);
}

function rangesOverlap(
  leftStart: number,
  leftEnd: number,
  rightStart: number,
  rightEnd: number,
  slackMs: number,
): boolean {
  return leftEnd + slackMs >= rightStart && leftStart - slackMs <= rightEnd;
}

function normalizedMinisterScope(minister: string): string | null {
  return scopeKeyForMinister(minister) ?? normalizeReference(minister);
}

export function formatLastRun(scope: ScopeConfig, health: SystemHealth | null): string {
  if (scope.kind !== 'minister') return '';
  if (scope.status !== 'live') return 'Not wired';
  if (!health) return 'Last run loading...';

  const trace = health.traces.find(item => isScopeMinister(item.minister, scope));

  if (!trace) return formatPersistedOutput(scope, health) ?? 'Last run never';
  const timestamp = trace.completedAt ?? trace.startedAt;
  const label = trace.status === 'running' ? 'Running since' : 'Last run';
  return `${label} ${formatTraceTime(timestamp)}`;
}

function formatPersistedOutput(scope: ScopeConfig, health: SystemHealth): string | null {
  const snapshot = health.minister_outputs.snapshots.find(item => isScopeMinister(item.minister, scope));
  if (!snapshot) return null;
  if (snapshot.persisted_at) return `Last output ${formatTraceTime(snapshot.persisted_at)}`;
  if (snapshot.state && snapshot.state !== 'available') return `Snapshot ${snapshot.state.replace(/_/g, ' ')}`;
  return null;
}

export function isScopeMinister(minister: string | null | undefined, scope: ScopeConfig): boolean {
  if (!minister) return false;
  const normalized = normalizeMinisterReference(minister);
  return ministerAliases(scope).some(alias => normalizeMinisterReference(alias) === normalized);
}

export function valueForScope<T>(values: Record<string, T>, scope: ScopeConfig): T | undefined {
  for (const alias of ministerAliases(scope)) {
    const exact = values[alias];
    if (exact !== undefined) return exact;
  }

  return Object.entries(values).find(([key]) => isScopeMinister(key, scope))?.[1];
}

function ministerAliases(scope: ScopeConfig): string[] {
  if (scope.key === 'food') return [scope.key, scope.label, 'Food', 'Chef', 'chef'];
  return [scope.key, scope.label];
}

function normalizeMinisterReference(value: string): string {
  return value.trim().replace(/[\s-]+/g, '_').toLocaleLowerCase();
}

function formatTraceTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const now = new Date();
  const sameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();

  return sameDay
    ? date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function logEntryForApplyAttempt(attempt: AssistedApplyAttempt): LogEntry {
  const statusLabel = applyStatusLabel(attempt.status);
  const actionLabel = attempt.kind ? humanize(attempt.kind) : 'Action';
  const message = cleanMessage(attempt.message) || actionLabel;

  return {
    id: `apply:${attempt.advice_id}:${attempt.action_index}:${attempt.at}`,
    at: attempt.at,
    kind: 'action_applied',
    tone: toneForApplyStatus(attempt.status),
    title: `${statusLabel} - ${message}`,
    icon: '✅',
    chips: [
      { emoji: '🖱️', label: actionLabel, tooltip: `Apply kind: ${attempt.kind ?? 'unknown'}` },
      { emoji: emojiForTone(toneForApplyStatus(attempt.status)), label: humanize(attempt.status), tooltip: `Apply status: ${attempt.status}` },
    ],
    link: logLink('system', 'events', 'Open System Events'),
    tooltip: `Advice ${attempt.advice_id}, action ${attempt.action_index}: ${attempt.message}`,
  };
}

function logEntryForCabinetRun(
  run: CabinetRunLogSnapshot,
  activeAdvice: AdviceItem[],
  flags: Record<string, AgentFlag[]>,
): LogEntry {
  const stepCount = countSteps(run.steps);
  const duration = formatDuration(run.duration_ms);
  const titleTail = stepCount > 0 ? `${stepCount} ${stepCount === 1 ? 'step' : 'steps'}` : humanize(run.trigger);

  return {
    id: `cabinet:${run.run_id}`,
    at: run.completed_at ?? run.started_at,
    kind: 'cabinet_run',
    tone: toneForCabinetStatus(run.status),
    title: `Cabinet run ${cabinetStatusLabel(run.status)} - ${titleTail}`,
    icon: '🏛️',
    chips: [
      { emoji: '📋', label: `${stepCount}`, tooltip: `${stepCount} cabinet steps recorded` },
      { emoji: '⏱️', label: duration, tooltip: `Duration: ${duration}` },
      { emoji: emojiForTone(toneForCabinetStatus(run.status)), label: humanize(run.status), tooltip: `Run status: ${run.status}` },
    ],
    link: logLink('home', 'overview', 'Open CABINET overview'),
    steps: run.steps.map(step => logStepForCabinetStep(step, activeAdvice, flags)),
    tooltip: [
      `Run ${run.run_id}`,
      `Scope: ${run.scope}`,
      `Trigger: ${run.trigger}`,
      `Status: ${run.status}`,
      run.error_message ? `Error: ${run.error_message}` : null,
    ].filter(Boolean).join(' | '),
  };
}

function logStepForCabinetStep(
  step: CabinetRunStepSnapshot,
  activeAdvice: AdviceItem[],
  flags: Record<string, AgentFlag[]>,
): LogStep {
  const ruleNames = ruleNamesFromTrace(step.rule_fired);
  const advice = matchingAdviceForStep(step, ruleNames, activeAdvice);
  const stepFlags = matchingFlagsForStep(step, flags);
  const adviceCount = advice.length > 0 || step.kind === 'solver'
    ? advice.length
    : step.advice_count;
  const flagCount = stepFlags.length > 0 || step.kind === 'solver'
    ? stepFlags.length
    : step.flag_count;
  const optionsCount = advice.length === 0
    ? null
    : advice.reduce((count, item) => count + (item.options?.length ?? 0), 0);
  const tone = logStepTone(step, optionsCount);

  return {
    advice: advice.map(adviceItemForLog),
    adviceCount,
    id: `${step.key}:${step.started_at}`,
    at: step.completed_at ?? step.started_at,
    children: step.children.map(child => logStepForCabinetStep(child, activeAdvice, flags)),
    detail: step.error_message ?? step.detail ?? step.trace_note,
    durationMs: step.duration_ms,
    flags: stepFlags.map(flagItemForLog),
    flagCount,
    kind: step.kind,
    label: step.label,
    link: logLinkForCabinetStep(step, advice, ruleNames),
    optionsCount,
    ruleNames,
    status: step.status,
    tone,
  };
}

function logStepTone(step: CabinetRunStepSnapshot, optionsCount: number | null): LogTone {
  const status = normalizeReference(step.status);
  if (status === 'failed' || status === 'error') return 'error';
  if (status === 'running') return 'info';

  if (step.kind === 'solver' && (optionsCount === 0 || hasSolverProblemDetail(step))) {
    return 'warn';
  }

  if (status === 'completed' || status === 'succeeded' || status === 'success') return 'ok';
  return 'warn';
}

function hasSolverProblemDetail(step: CabinetRunStepSnapshot): boolean {
  const text = [
    step.detail,
    step.trace_note,
    step.error_type,
    step.error_message,
  ].filter(Boolean).join(' ').toLocaleLowerCase();

  return [
    'no option',
    'no-option',
    'no fit',
    'no-fit',
    'no buildable',
    'no reachable',
    'could not',
    'blocked',
    'failed',
    'error',
    'unavailable',
    'inconclusive',
  ].some(fragment => text.includes(fragment));
}

function ruleNamesFromTrace(ruleFired: string | null): string[] {
  if (!ruleFired) return [];
  const withoutPrefix = ruleFired.replace(/^rules:/i, '');
  return withoutPrefix
    .split('+')
    .map(part => normalizeReference(part))
    .filter(Boolean);
}

function matchingAdviceForStep(
  step: CabinetRunStepSnapshot,
  ruleNames: string[],
  activeAdvice: AdviceItem[],
): AdviceItem[] {
  const minister = normalizeReference(step.minister ?? '');
  if (!minister) return [];
  const ministerAdvice = activeAdvice.filter(item => normalizeReference(item.minister) === minister);

  if (step.kind === 'solver') {
    const solverRuleName = solverRuleNameForStepLabel(step.label);
    if (!solverRuleName) return [];

    return ministerAdvice.filter(item => normalizeReference(item.id).includes(solverRuleName));
  }

  if (ruleNames.length === 0) return [];

  const matchedByRule = ministerAdvice.filter(item => {
    const id = normalizeReference(item.id);
    return ruleNames.some(ruleName => id.includes(ruleName));
  });

  return matchedByRule;
}

function solverRuleNameForStepLabel(label: string): string | null {
  const normalizedLabel = normalizeReference(label);

  if (normalizedLabel.includes('kitchen')) return 'kitchen_missing';
  if (normalizedLabel.includes('hospital')) return 'hospital_missing';
  if (normalizedLabel.includes('storage') || normalizedLabel.includes('stockpile')) return 'storage_room_missing';
  if (normalizedLabel.includes('growing') || normalizedLabel.includes('grow') || normalizedLabel.includes('zone')) {
    return 'zone_request_active';
  }
  if (normalizedLabel.includes('barracks') || normalizedLabel.includes('bed') || normalizedLabel.includes('building')) {
    return 'building_request_active';
  }

  return null;
}

function matchingFlagsForStep(
  step: CabinetRunStepSnapshot,
  flags: Record<string, AgentFlag[]>,
): AgentFlag[] {
  const minister = normalizeReference(step.minister ?? '');
  if (!minister) return [];

  return Object.values(flags)
    .flat()
    .filter(flag => normalizeReference(flag.source_minister) === minister);
}

function adviceItemForLog(item: AdviceItem): LogNamedItem {
  return {
    label: item.title,
    tooltip: `${item.id}: ${item.title}`,
  };
}

function flagItemForLog(flag: AgentFlag): LogNamedItem {
  return {
    label: flag.summary || flag.id,
    tooltip: `${flag.id}: ${flag.summary}`,
  };
}

function logEntryForTrace(trace: MinisterTrace): LogEntry | null {
  if (trace.escalationReason || trace.path === 'llm' || trace.path === 'llm_failed') {
    return logEntryForEscalationTrace(trace);
  }

  if (normalizeReference(trace.trigger) === 'manualtrigger') {
    return logEntryForManualMinisterRun(trace);
  }

  return null;
}

function logEntryForEscalationTrace(trace: MinisterTrace): LogEntry {
  const minister = displayMinisterName(trace.minister);
  const tone = toneForTrace(trace);
  const pathLabel = pathLabelForTrace(trace);
  const completedAt = trace.completedAt ?? trace.startedAt;
  const outputChip = formatOutputChip(trace);

  return {
    id: `trace:${normalizeReference(trace.minister)}:${completedAt}:${trace.path}:${trace.escalationReason ?? ''}`,
    at: completedAt,
    kind: 'escalation',
    tone,
    title: `${minister} ${pathLabel}`,
    icon: trace.path === 'llm_failed' ? '⚠️' : '🧠',
    chips: [
      { emoji: ministerEmoji(trace.minister), label: minister, tooltip: `Minister: ${minister}` },
      { emoji: trace.path === 'llm_failed' ? '❌' : '🧠', label: humanize(trace.path), tooltip: `Trace path: ${trace.path}` },
      ...(outputChip ? [outputChip] : []),
    ],
    link: logLinkForMinister(trace.minister, targetViewForTrace(trace), `Open ${minister} ${targetViewForTrace(trace)}`),
    tooltip: [
      trace.escalationReason ? `Escalation: ${trace.escalationReason}` : null,
      trace.errorMessage ? `Error: ${trace.errorMessage}` : null,
      trace.note ? `Note: ${trace.note}` : null,
    ].filter(Boolean).join(' | ') || `${minister} trace path: ${trace.path}`,
  };
}

function logEntryForManualMinisterRun(trace: MinisterTrace): LogEntry {
  const minister = displayMinisterName(trace.minister);
  const completedAt = trace.completedAt ?? trace.startedAt;
  const outputChip = formatOutputChip(trace);
  const status = normalizeReference(trace.status);
  const mode = trace.path === 'rules' ? 'rules' : humanize(trace.path).toLocaleLowerCase();
  const result = status === 'completed' ? 'complete' : humanize(trace.status).toLocaleLowerCase();

  return {
    id: `minister-run:${normalizeReference(trace.minister)}:${completedAt}:${trace.path}:${trace.status}`,
    at: completedAt,
    kind: 'minister_run',
    tone: toneForMinisterRunStatus(trace.status),
    title: `${minister} ${mode} run ${result}`,
    icon: '📏',
    chips: [
      { emoji: ministerEmoji(trace.minister), label: minister, tooltip: `Minister: ${minister}` },
      { emoji: trace.path === 'rules' ? '📏' : '🧠', label: humanize(trace.path), tooltip: `Trace path: ${trace.path}` },
      { emoji: emojiForTone(toneForMinisterRunStatus(trace.status)), label: humanize(trace.status), tooltip: `Run status: ${trace.status}` },
      ...(outputChip ? [outputChip] : []),
    ],
    link: logLinkForMinister(trace.minister, targetViewForTrace(trace), `Open ${minister} ${targetViewForTrace(trace)}`),
    tooltip: [
      `Trigger: ${trace.trigger}`,
      `Path: ${trace.path}`,
      `Status: ${trace.status}`,
      trace.errorMessage ? `Error: ${trace.errorMessage}` : null,
      trace.note ? `Note: ${trace.note}` : null,
    ].filter(Boolean).join(' | '),
  };
}

function logEntryForCriticalAdvice(advice: AdviceItem): LogEntry {
  const minister = displayMinisterName(advice.minister);
  const actionCount = advice.actions.length;

  return {
    id: `advice:${advice.id}`,
    at: advice.stamp.issued_at,
    kind: 'critical_advice',
    tone: 'warn',
    title: `Critical - ${advice.title}`,
    icon: '⚠️',
    chips: [
      { emoji: ministerEmoji(advice.minister), label: minister, tooltip: `Minister: ${minister}` },
      { emoji: '💡', label: `${actionCount}`, tooltip: `${actionCount} advice actions` },
    ],
    link: logLinkForMinister(advice.minister, 'advice', `Open ${minister} advice`, advice.id),
    tooltip: `${minister}: ${advice.title} | ${advice.rationale}`,
  };
}

function logLinkForCabinetStep(
  step: CabinetRunStepSnapshot,
  advice: AdviceItem[],
  ruleNames: string[],
): LogLink | undefined {
  if (advice.length > 0) {
    return logLinkForMinister(step.minister ?? '', 'advice', 'Open related advice', advice[0].id);
  }

  const scope = scopeKeyForMinister(step.minister ?? '');
  if (!scope) return undefined;

  if (step.kind === 'solver') {
    const view: DashboardViewKey = scope === 'willie' ? 'solver' : 'advice';
    return logLink(scope, view, `Open ${displayMinisterName(step.minister ?? '')} ${view}`);
  }

  if (ruleNames.length > 0) {
    return logLink(scope, 'rules', `Open ${displayMinisterName(step.minister ?? '')} rules`);
  }

  return logLink(scope, 'advice', `Open ${displayMinisterName(step.minister ?? '')} advice`);
}

function logLinkForMinister(
  minister: string,
  view: DashboardViewKey,
  tooltip: string,
  anchorId?: string,
): LogLink | undefined {
  const scope = scopeKeyForMinister(minister);
  if (!scope) return undefined;
  return logLink(scope, view, tooltip, anchorId);
}

function logLink(
  scope: ScopeKey,
  view: DashboardViewKey,
  tooltip: string,
  anchorId?: string,
): LogLink {
  return {
    ariaLabel: tooltip,
    href: dashboardHref(scope, view, anchorId),
    tooltip,
  };
}

function dashboardHref(scope: ScopeKey, view: DashboardViewKey, anchorId?: string): string {
  const query = `/?scope=${encodeURIComponent(scope)}&view=${encodeURIComponent(view)}`;
  return anchorId ? `${query}#${encodeURIComponent(anchorId)}` : query;
}

function targetViewForTrace(trace: MinisterTrace): DashboardViewKey {
  if (trace.path === 'rules') return 'rules';
  if (trace.path === 'llm' || trace.path === 'llm_failed') return 'prompt';
  return 'advice';
}

function scopeKeyForMinister(minister: string): ScopeKey | null {
  const normalized = normalizeReference(minister);
  if (normalized === 'food' || normalized === 'chef') return 'food';
  if (normalized === 'willie' || normalized === 'construction') return 'willie';
  if (normalized === 'mayor') return 'mayor';
  if (normalized === 'welfare' || normalized === 'minister_of_welfare') return 'welfare';
  if (normalized === 'defense') return 'defense';
  if (normalized === 'medical') return 'medical';
  if (normalized === 'research') return 'research';
  if (normalized === 'industry') return 'industry';
  if (normalized === 'economy') return 'economy';
  if (normalized === 'chief_of_staff' || normalized === 'cos' || normalized === 'chief') return 'chief_of_staff';
  return null;
}

function toneForApplyStatus(status: string): LogTone {
  const normalized = normalizeReference(status);
  if (normalized === 'applied' || normalized === 'already_satisfied') return 'ok';
  if (normalized === 'stale_advice' || normalized === 'validation_failed') return 'warn';
  if (normalized.includes('rimapi') || normalized.includes('rejected') || normalized.includes('error') || normalized.includes('failed')) return 'error';
  return 'info';
}

function toneForCabinetStatus(status: string): LogTone {
  const normalized = normalizeReference(status);
  if (normalized === 'completed' || normalized === 'succeeded' || normalized === 'success') return 'ok';
  if (normalized === 'failed' || normalized === 'error') return 'error';
  if (normalized === 'running') return 'info';
  return 'warn';
}

function toneForTrace(trace: MinisterTrace): LogTone {
  if (trace.path === 'llm_failed') return 'error';
  if (trace.path === 'llm') return 'ok';
  return trace.escalationReason ? 'warn' : 'info';
}

function toneForMinisterRunStatus(status: string): LogTone {
  const normalized = normalizeReference(status);
  if (normalized === 'completed' || normalized === 'succeeded' || normalized === 'success') return 'ok';
  if (normalized === 'failed' || normalized === 'error') return 'error';
  if (normalized === 'running') return 'info';
  return 'warn';
}

function applyStatusLabel(status: string): string {
  const normalized = normalizeReference(status);
  if (normalized === 'applied') return 'Applied';
  if (normalized === 'already_satisfied') return 'Already satisfied';
  if (normalized === 'stale_advice') return 'Stale advice';
  if (normalized === 'validation_failed') return 'Validation failed';
  if (normalized.includes('rimapi')) return 'RIMAPI blocked';
  return humanize(status);
}

function cabinetStatusLabel(status: string): string {
  const normalized = normalizeReference(status);
  if (normalized === 'completed' || normalized === 'succeeded' || normalized === 'success') return 'complete';
  if (normalized === 'running') return 'running';
  if (normalized === 'failed' || normalized === 'error') return 'failed';
  return humanize(status).toLocaleLowerCase();
}

function pathLabelForTrace(trace: MinisterTrace): string {
  if (trace.path === 'llm_failed') return 'LLM failed';
  if (trace.path === 'llm') return 'used LLM';
  if (trace.escalationReason) return 'requested LLM';
  return humanize(trace.path);
}

function formatOutputChip(trace: MinisterTrace): LogChip | null {
  const adviceCount = trace.adviceCount ?? 0;
  const flagCount = trace.flagCount ?? 0;
  if (adviceCount === 0 && flagCount === 0) return null;
  return {
    emoji: '📣',
    label: `${adviceCount}/${flagCount}`,
    tooltip: `${adviceCount} advice items, ${flagCount} flags`,
  };
}

function countSteps(steps: CabinetRunLogSnapshot['steps']): number {
  return steps.reduce((count, step) => count + 1 + countSteps(step.children ?? []), 0);
}

function formatDuration(durationMs: number | null): string {
  if (durationMs === null || !Number.isFinite(durationMs)) return 'n/a';
  if (durationMs < 1000) return `${Math.max(0, Math.round(durationMs))}ms`;
  const seconds = durationMs / 1000;
  if (seconds < 60) return `${seconds.toFixed(seconds >= 10 ? 0 : 1)}s`;
  const minutes = Math.floor(seconds / 60);
  const remainingSeconds = Math.round(seconds % 60);
  return `${minutes}m ${remainingSeconds}s`;
}

function emojiForTone(tone: LogTone): string {
  if (tone === 'ok') return '✅';
  if (tone === 'warn') return '⚠️';
  if (tone === 'error') return '❌';
  return 'ℹ️';
}

function displayMinisterName(minister: string): string {
  const normalized = normalizeReference(minister);
  if (normalized === 'food' || normalized === 'chef') return 'Chef';
  if (normalized === 'willie' || normalized === 'construction') return 'Willie';
  if (normalized === 'mayor') return 'Mayor';
  if (normalized === 'welfare') return 'Welfare';
  if (normalized === 'chief_of_staff') return 'Chief of Staff';
  return humanize(minister);
}

function ministerEmoji(minister: string): string {
  const normalized = normalizeReference(minister);
  if (normalized === 'food' || normalized === 'chef') return '🍲';
  if (normalized === 'willie' || normalized === 'construction') return '🧱';
  if (normalized === 'mayor') return '🏛️';
  if (normalized === 'welfare') return '🙂';
  if (normalized === 'defense') return '🛡️';
  if (normalized === 'medical') return '🩺';
  if (normalized === 'research') return '🔬';
  if (normalized === 'industry') return '⚙️';
  if (normalized === 'economy') return '🪙';
  return '📣';
}

function cleanMessage(message: string): string {
  return message.trim().replace(/\s+/g, ' ');
}

function humanize(value: string): string {
  const words = normalizeReference(value).split('_').filter(Boolean);
  if (words.length === 0) return value;
  return words.map(word => word.slice(0, 1).toLocaleUpperCase() + word.slice(1)).join(' ');
}

function normalizeReference(value: string): string {
  return value.trim().replace(/[\s-]+/g, '_').toLocaleLowerCase();
}

function timestampOf(iso: string): number {
  const value = new Date(iso).getTime();
  return Number.isNaN(value) ? 0 : value;
}
