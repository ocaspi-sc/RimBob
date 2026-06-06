import { useEffect, useState, type ReactNode } from 'react';
import { applyAdviceAction } from '../../api/advice';
import { formatMaybeDate } from '../../dashboard/connectivityStatus';
import { formatLastRun, isScopeMinister, valueForScope } from '../../dashboard/selectors';
import {
  scopeConfigs,
  viewsForScope,
  type DashboardViewDefinition,
  type DashboardViewKey,
  type ScopeConfig,
  type ScopeKey,
} from '../../dashboard/scopes';
import {
  iconForActionKind,
  iconForField,
  iconForScope,
  iconForView,
  type SemanticIconSpec,
} from '../../dashboard/semanticIcons';
import type { MayorAgenda } from '../../types/agenda';
import type {
  AdviceAction,
  AdviceApplyResponse,
  AdviceItem,
  AgentFlag,
  AttentionRequest,
  BuildingRequest,
  ItemRequest,
  LaborRequest,
  Priority,
  SuggestedAction,
  ZoneRequest,
} from '../../types/advice';
import type { ColonySnapshot } from '../../types/colony';
import type { CabinetRunLogSnapshot, MinisterTrace, SystemHealth } from '../../types/system';
import { MetricCard } from '../shared/MetricCard';
import { SemanticIconCue, SemanticLabel } from '../shared/SemanticIcon';
import { StatusPill, type PillTone } from '../shared/StatusPill';

type MetricTone = 'neutral' | 'ok' | 'warn' | 'error';

type HomeOutputSection = {
  iconKey: string;
  items: HomeOutputItem[];
  key: string;
  label: string;
};

type HomeOutputItem = {
  applyStatus?: HomeActionApplyStatus | null;
  iconKey?: string | null;
  key: string;
  priority: Priority;
  title: string;
  tooltip: string;
};

type HomeAdviceSummary = {
  icon: SemanticIconSpec | undefined;
  title: string;
  tone: MetricTone;
};

type HomeActionDisplay = {
  iconKey: string | null;
  title: string;
};

type HomeActionApplyStatus = {
  ariaLabel: string;
  label: string;
  tone: 'available' | 'queued' | 'blocked' | 'applied';
  tooltip: string;
};

type HomePossibleAction = {
  actionIndex: number;
  adviceId: string;
  applyLabel: string;
  applyTarget: string;
  iconKey: string | null;
  key: string;
  ministerScope: ScopeConfig;
  priority: Priority;
  title: string;
  tooltip: string;
};

type HomeAppliedAction = {
  actionIndex: number;
  adviceId: string;
  iconKey: string | null;
  key: string;
  ministerScope: ScopeConfig;
  priority: Priority;
  recordedAt: string;
  recordedAtMs: number;
  resultMessage: string;
  resultStatus: string;
  title: string;
  tooltip: string;
};

type HomeAdviceExpiryState = {
  expired: boolean;
  message: string | null;
};

type HomePossibleActionApplyState = {
  error: string | null;
  response: AdviceApplyResponse | null;
  status: 'idle' | 'pending' | 'done' | 'error';
};

type HomeAppliedActionFilter = 'all' | 'day' | 'fresh';

const AppliedActionHistoryStorageKey = 'rimbob.dashboard.cabinet.appliedActions';
const AppliedActionHistoryLimit = 100;

export function HomeOverview({
  advice,
  agenda,
  cabinetBusy,
  cabinetPending,
  cabinetRulesPending,
  flags,
  health,
  hostApiLive,
  onRunCabinet,
  onRunCabinetRules,
  onSelectMinisterView,
  recentCabinetRuns,
  snapshot,
  stateSummaries,
  triggerError,
}: {
  advice: AdviceItem[];
  agenda: MayorAgenda | null;
  cabinetBusy: boolean;
  cabinetPending: boolean;
  cabinetRulesPending: boolean;
  flags: Record<string, AgentFlag[]>;
  health: SystemHealth | null;
  hostApiLive: boolean;
  onRunCabinet: () => void;
  onRunCabinetRules: () => void;
  onSelectMinisterView: (scope: ScopeKey, view: DashboardViewKey) => void;
  recentCabinetRuns: CabinetRunLogSnapshot[];
  snapshot: ColonySnapshot | null;
  stateSummaries: Record<string, string>;
  triggerError: string | null;
}) {
  const latestCabinetRun = recentCabinetRuns[0] ?? null;
  const liveMinisters = scopeConfigs.filter(scope => scope.kind === 'minister' && scope.status === 'live');
  const currentGameTick = snapshot?.gameTick ?? null;
  const possibleActions = possibleApplyActions(advice, liveMinisters, currentGameTick);
  const observedAppliedActions = appliedActionsFromAdvice(advice, liveMinisters);
  const observedAppliedActionSignature = observedAppliedActions
    .map(action => `${action.key}:${action.resultStatus}:${action.recordedAt}`)
    .join('|');
  const latestRegenAt = latestAdviceIssuedAt(advice);
  const [appliedActionHistory, setAppliedActionHistory] = useState<Record<string, HomeAppliedAction>>(
    loadAppliedActionHistory,
  );
  const recentlyAppliedActions = Object.values(appliedActionHistory)
    .sort((left, right) => right.recordedAtMs - left.recordedAtMs || left.title.localeCompare(right.title));
  const cabinetGeneratedLabel = formatGeneratedAtLabel(health?.generated_at ?? null);
  const disableCabinetControls = cabinetBusy || !hostApiLive;

  useEffect(() => {
    if (observedAppliedActions.length === 0) return;

    setAppliedActionHistory(current => mergeAppliedActionHistory(current, observedAppliedActions));
  }, [observedAppliedActionSignature]);

  useEffect(() => {
    saveAppliedActionHistory(appliedActionHistory);
  }, [appliedActionHistory]);

  const onCabinetActionApplied = (action: HomeAppliedAction) => {
    setAppliedActionHistory(current => mergeAppliedActionHistory(current, [action]));
  };

  return (
    <div className="home-overview">
      <section className="home-command-bar" aria-label="CABINET command bar">
        <div className="home-command-title">
          <span className="eyebrow">
            <SemanticIconCue icon={iconForScope('home')} size="xs" />
            CABINET
          </span>
          <h2>RimBob Cabinet Summary - Generated at {cabinetGeneratedLabel}</h2>
        </div>
        <div className="home-command-actions">
          <button
            aria-busy={cabinetPending}
            className="trigger-button global"
            disabled={disableCabinetControls}
            onClick={onRunCabinet}
            title="Run all currently wired live ministers now."
            type="button"
          >
            <SemanticIconCue icon={iconForField('cabinet')} size="xs" />
            {cabinetPending ? 'Running Cabinet...' : 'Run Cabinet Now'}
          </button>
          <button
            aria-busy={cabinetRulesPending}
            className="trigger-button secondary"
            disabled={disableCabinetControls}
            onClick={onRunCabinetRules}
            title="Run every wired rules-capable minister's deterministic path; skips the LLM-only Mayor."
            type="button"
          >
            <SemanticIconCue icon={iconForView('rules')} size="xs" />
            {cabinetRulesPending ? 'Running Rules...' : 'Run Cabinet (Rules Only)'}
          </button>
          {triggerError && <span className="trigger-error" role="status">{triggerError}</span>}
        </div>
      </section>

      <section className="home-section" aria-label="Possible actions">
        <SectionHeading iconKey="assisted_apply" title="Possible actions" meta={`${possibleActions.length} ready now`} />
        <PossibleActionsPanel
          actions={possibleActions}
          onActionApplied={onCabinetActionApplied}
          onSelectMinisterView={onSelectMinisterView}
        />
      </section>

      <section className="home-section" aria-label="Recently applied actions">
        <SectionHeading
          iconKey="assisted_apply"
          title="Recently Applied Actions"
          meta={`${recentlyAppliedActions.length} recorded`}
        />
        <RecentlyAppliedActionsPanel
          actions={recentlyAppliedActions}
          latestRegenAt={latestRegenAt}
          onSelectMinisterView={onSelectMinisterView}
        />
      </section>

      <section className="home-section" aria-label="Pipeline vitals">
        <SectionHeading iconKey="runtime" title="Pipeline Vitals" />
        <div className="home-vitals-grid">
          <MetricCard
            label={<MetricLabel iconKey="source">State origin</MetricLabel>}
            note={health ? `generated ${formatMaybeDate(health.generated_at)}` : 'health loading'}
            tone={originTone(health?.runtime.colony_state_origin)}
            value={health?.runtime.colony_state_origin ?? 'loading'}
          />
          <MetricCard
            label={<MetricLabel iconKey="advice">Active advice</MetricLabel>}
            tone={(health?.runtime.active_advice_count ?? advice.length) > 0 ? 'ok' : 'neutral'}
            value={formatInteger(health?.runtime.active_advice_count ?? advice.length)}
          />
          <MetricCard
            label={<MetricLabel iconKey="flags">Active flags</MetricLabel>}
            tone={(health?.runtime.active_flag_count ?? activeFlagCount(flags)) > 0 ? 'warn' : 'neutral'}
            value={formatInteger(health?.runtime.active_flag_count ?? activeFlagCount(flags))}
          />
          <MetricCard
            label={<MetricLabel iconKey="agenda">Agenda version</MetricLabel>}
            note={agenda ? `generated ${formatMaybeDate(agenda.generated_at)}` : 'agenda loading'}
            value={agenda?.version ?? health?.runtime.mayor_snapshot_version ?? 'n/a'}
          />
          <MetricCard
            label={<MetricLabel iconKey="cabinet">Cabinet last run</MetricLabel>}
            note={latestCabinetRun ? formatMaybeDate(latestCabinetRun.completed_at ?? latestCabinetRun.started_at) : 'no run event'}
            tone={cabinetRunTone(latestCabinetRun)}
            value={formatCabinetRun(latestCabinetRun)}
          />
        </div>
      </section>

      <section className="home-section" aria-label="Live ministers">
        <SectionHeading iconKey="minister" title="Live Ministers" />
        <div className="home-minister-grid">
          {liveMinisters.map(scope => (
            <MinisterSummaryCard
              advice={advice}
              agenda={agenda}
              flags={flags}
              health={health}
              hostApiLive={hostApiLive}
              currentGameTick={currentGameTick}
              key={scope.key}
              onSelectMinisterView={onSelectMinisterView}
              scope={scope}
              stateSummaries={stateSummaries}
            />
          ))}
        </div>
      </section>
    </div>
  );
}

function SectionHeading({ iconKey, meta, title }: { iconKey: string; meta?: string; title: string }) {
  return (
    <div className="home-section-heading">
      <h3>
        <SemanticLabel icon={iconForField(iconKey)}>
          <span>{title}</span>
        </SemanticLabel>
      </h3>
      {meta && <small className="home-section-meta">{meta}</small>}
    </div>
  );
}

function MetricLabel({ children, iconKey }: { children: ReactNode; iconKey: string }) {
  return (
    <SemanticLabel icon={iconForField(iconKey)}>
      <span>{children}</span>
    </SemanticLabel>
  );
}

function PossibleActionsPanel({
  actions,
  onActionApplied,
  onSelectMinisterView,
}: {
  actions: HomePossibleAction[];
  onActionApplied: (action: HomeAppliedAction) => void;
  onSelectMinisterView: (scope: ScopeKey, view: DashboardViewKey) => void;
}) {
  const [applyState, setApplyState] = useState<Record<string, HomePossibleActionApplyState>>({});

  const onApplyAction = async (action: HomePossibleAction) => {
    setApplyState(current => ({
      ...current,
      [action.key]: { status: 'pending', response: null, error: null },
    }));

    try {
      const response = await applyAdviceAction(action.adviceId, action.actionIndex);
      setApplyState(current => ({
        ...current,
        [action.key]: { status: 'done', response, error: null },
      }));
      if (isApplySuccess(response.status)) {
        onActionApplied(appliedActionFromPossibleAction(action, response));
      }
    } catch (error) {
      setApplyState(current => ({
        ...current,
        [action.key]: { status: 'error', response: null, error: String(error) },
      }));
    }
  };

  if (actions.length === 0) {
    return (
      <div className="home-possible-actions-empty" role="status">
        <SemanticIconCue icon={iconForField('assisted_apply')} size="xs" />
        <span>No apply-ready actions</span>
      </div>
    );
  }

  return (
    <div className="home-possible-actions-table-shell">
      <table className="home-possible-actions-table">
        <colgroup>
          <col className="home-possible-actions-minister-col" />
          <col className="home-possible-actions-title-col" />
          <col className="home-possible-actions-subtitle-col" />
          <col className="home-possible-actions-apply-col" />
        </colgroup>
        <thead>
          <tr>
            <th scope="col">Minister</th>
            <th scope="col">Title</th>
            <th scope="col">Subtitle</th>
            <th scope="col">Apply</th>
          </tr>
        </thead>
        <tbody>
          {actions.map(action => {
            const state = applyState[action.key] ?? { status: 'idle' as const, response: null, error: null };
            const success = isApplySuccess(state.response?.status);
            const resultMessage = state.response?.message ?? state.error ?? null;
            const resultStatus = state.response?.status ?? state.status;
            const disabled = state.status === 'pending' || success;

            return (
              <tr
                className={`home-possible-action-row priority-${action.priority}`}
                key={action.key}
                title={action.tooltip}
              >
                <td className="home-possible-action-minister">
                  <span>
                    {action.ministerScope.emoji ? (
                      <span className="scope-title-emoji" aria-hidden="true">{action.ministerScope.emoji}</span>
                    ) : (
                      <SemanticIconCue icon={iconForScope(action.ministerScope.key)} size="xs" />
                    )}
                    <strong>{action.ministerScope.displayLabel}</strong>
                  </span>
                </td>
                <td className="home-possible-action-title">
                  <span>
                    <SemanticIconCue
                      icon={iconForActionKind(action.iconKey) ?? iconForField(action.iconKey ?? 'actions')}
                      size="xs"
                    />
                    <strong>{shortTitle(action.title, 48)}</strong>
                  </span>
                </td>
                <td
                  className="home-possible-action-subtitle"
                  title={`${action.applyLabel}: ${action.applyTarget}`}
                >
                  <span>{shortTitle(action.applyTarget, 72)}</span>
                  {resultMessage && (
                    <small className={`home-possible-action-result ${resultStatus}`}>
                      {resultMessage}
                    </small>
                  )}
                </td>
                <td className="home-possible-action-apply">
                  <button
                    aria-label={`Apply ${action.title} from ${action.ministerScope.displayLabel}`}
                    className="home-possible-action-apply-button"
                    disabled={disabled}
                    onClick={() => void onApplyAction(action)}
                    title={`${action.applyLabel}: ${action.applyTarget}`}
                    type="button"
                  >
                    <SemanticIconCue icon={iconForField('assisted_apply')} size="xs" />
                    <span>{possibleActionApplyLabel(action, state)}</span>
                  </button>
                  <button
                    aria-label={`Open ${action.ministerScope.displayLabel} Advice`}
                    className="home-possible-action-open-button"
                    onClick={() => onSelectMinisterView(action.ministerScope.key, 'advice')}
                    title={`Open ${action.ministerScope.displayLabel} Advice.`}
                    type="button"
                  >
                    <SemanticIconCue icon={iconForView('advice')} size="xs" />
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function RecentlyAppliedActionsPanel({
  actions,
  latestRegenAt,
  onSelectMinisterView,
}: {
  actions: HomeAppliedAction[];
  latestRegenAt: Date | null;
  onSelectMinisterView: (scope: ScopeKey, view: DashboardViewKey) => void;
}) {
  const [filter, setFilter] = useState<HomeAppliedActionFilter>('all');
  const filteredActions = filteredAppliedActions(actions, filter, latestRegenAt);

  return (
    <div className="home-applied-actions-panel">
      <div className="home-applied-action-filters" role="group" aria-label="Recently applied action filters">
        {(['all', 'day', 'fresh'] as HomeAppliedActionFilter[]).map(option => (
          <button
            aria-pressed={filter === option}
            className={filter === option ? 'active' : undefined}
            key={option}
            onClick={() => setFilter(option)}
            title={appliedActionFilterTitle(option, latestRegenAt)}
            type="button"
          >
            {appliedActionFilterLabel(option)}
          </button>
        ))}
      </div>
      {filteredActions.length === 0 ? (
        <div className="home-possible-actions-empty" role="status">
          <SemanticIconCue icon={iconForField('assisted_apply')} size="xs" />
          <span>No applied actions for this filter</span>
        </div>
      ) : (
        <div className="home-possible-actions-table-shell">
          <table className="home-possible-actions-table home-applied-actions-table">
            <colgroup>
              <col className="home-possible-actions-minister-col" />
              <col className="home-possible-actions-title-col" />
              <col className="home-applied-actions-result-col" />
              <col className="home-applied-actions-time-col" />
            </colgroup>
            <thead>
              <tr>
                <th scope="col">Minister</th>
                <th scope="col">Title</th>
                <th scope="col">Result</th>
                <th scope="col">Applied</th>
              </tr>
            </thead>
            <tbody>
              {filteredActions.map(action => (
                <tr
                  className={`home-possible-action-row priority-${action.priority}`}
                  key={action.key}
                  title={action.tooltip}
                >
                  <td className="home-possible-action-minister">
                    <span>
                      {action.ministerScope.emoji ? (
                        <span className="scope-title-emoji" aria-hidden="true">{action.ministerScope.emoji}</span>
                      ) : (
                        <SemanticIconCue icon={iconForScope(action.ministerScope.key)} size="xs" />
                      )}
                      <strong>{action.ministerScope.displayLabel}</strong>
                    </span>
                  </td>
                  <td className="home-possible-action-title">
                    <span>
                      <SemanticIconCue
                        icon={iconForActionKind(action.iconKey) ?? iconForField(action.iconKey ?? 'actions')}
                        size="xs"
                      />
                      <strong>{shortTitle(action.title, 48)}</strong>
                    </span>
                  </td>
                  <td className="home-applied-action-result">
                    <span>{shortTitle(action.resultMessage, 72)}</span>
                  </td>
                  <td className="home-applied-action-time">
                    <span>{formatMaybeDate(action.recordedAt)}</span>
                    <small>{formatAgeSince(new Date(action.recordedAt))} ago</small>
                    <button
                      aria-label={`Open ${action.ministerScope.displayLabel} Advice`}
                      className="home-possible-action-open-button"
                      onClick={() => onSelectMinisterView(action.ministerScope.key, 'advice')}
                      title={`Open ${action.ministerScope.displayLabel} Advice.`}
                      type="button"
                    >
                      <SemanticIconCue icon={iconForView('advice')} size="xs" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function MinisterSummaryCard({
  advice,
  agenda,
  flags,
  health,
  hostApiLive,
  currentGameTick,
  onSelectMinisterView,
  scope,
  stateSummaries,
}: {
  advice: AdviceItem[];
  agenda: MayorAgenda | null;
  flags: Record<string, AgentFlag[]>;
  health: SystemHealth | null;
  hostApiLive: boolean;
  currentGameTick: number | null;
  onSelectMinisterView: (scope: ScopeKey, view: DashboardViewKey) => void;
  scope: ScopeConfig;
  stateSummaries: Record<string, string>;
}) {
  const ministerAdvice = activeAdviceForScope(scope, advice);
  const ministerFlags = activeFlagsForScope(scope, flags);
  const adviceSummary = bottomLineAdvice(scope, ministerAdvice, stateSummaries, agenda);
  const outputSections = ministerOutputSections(ministerAdvice, ministerFlags, currentGameTick);
  const hasOutputSections = outputSections.length > 0;
  const rulesSummary = ruleSummary(scope, health, hostApiLive);

  return (
    <article
      className={`home-minister-card ${adviceSummary.tone} ${hasOutputSections ? '' : 'no-output'}`}
    >
      <div className="home-minister-card-header">
        <div className="home-minister-title-row">
          <span className="home-minister-identity">
            {scope.emoji ? (
              <span className="scope-title-emoji" aria-hidden="true">{scope.emoji}</span>
            ) : (
              <SemanticIconCue icon={iconForScope(scope.key)} size="sm" />
            )}
            <span>
              <strong>{scope.displayLabel}</strong>
              <small>{formatLastRun(scope, health)}</small>
            </span>
          </span>
          <div className="home-minister-card-tools">
            <MinisterTabLinks
              onSelectMinisterView={onSelectMinisterView}
              scope={scope}
            />
            <StatusPill tone={rulesSummary.tone}>{rulesSummary.status}</StatusPill>
          </div>
        </div>
        <span className="home-minister-advice-row" title={adviceSummary.title}>
          <SemanticIconCue icon={adviceSummary.icon} size="xs" />
          <span className="eyebrow">Advice</span>
          <strong>{adviceSummary.title}</strong>
        </span>
      </div>

      {hasOutputSections && (
        <div className="home-minister-card-body">
          {outputSections.map(section => (
            <MinisterOutputSection key={section.key} section={section} />
          ))}
        </div>
      )}
    </article>
  );
}

function MinisterOutputSection({ section }: { section: HomeOutputSection }) {
  return (
    <section className="home-minister-output-section">
      <h4>
        <SemanticIconCue icon={iconForField(section.iconKey)} size="xs" />
        <span>{section.label}</span>
      </h4>
      <ul>
        {section.items.map(item => (
          <li className={item.applyStatus ? 'has-apply-status' : undefined} key={item.key} title={item.tooltip}>
            <SemanticIconCue icon={outputItemIcon(section, item)} size="xs" />
            <span className="home-output-title">{shortTitle(item.title, 40)}</span>
            {item.applyStatus && (
              <span
                aria-label={item.applyStatus.ariaLabel}
                className={`home-output-apply-status ${item.applyStatus.tone}`}
                title={item.applyStatus.tooltip}
              >
                {item.applyStatus.label}
              </span>
            )}
            <span
              aria-label={`Priority: ${item.priority}`}
              className={`home-output-priority-bar ${item.priority}`}
              title={`Priority: ${item.priority}`}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}

function outputItemIcon(section: HomeOutputSection, item: HomeOutputItem) {
  let icon = iconForField(item.iconKey ?? section.iconKey);

  if (section.key === 'actions' || section.key === 'suggested_actions') {
    icon = iconForActionKind(item.iconKey) ?? icon;
  }

  return icon ?? iconForField(section.iconKey);
}

function MinisterTabLinks({
  onSelectMinisterView,
  scope,
}: {
  onSelectMinisterView: (scope: ScopeKey, view: DashboardViewKey) => void;
  scope: ScopeConfig;
}) {
  const tabViews = viewsForScope(scope);

  return (
    <nav className="home-minister-tabs" aria-label={`${scope.displayLabel} tabs`}>
      {tabViews.map(view => (
        <a
          className="home-minister-tab-link"
          href={`?scope=${encodeURIComponent(scope.key)}&view=${encodeURIComponent(view.key)}`}
          key={view.key}
          onClick={event => {
            event.preventDefault();
            onSelectMinisterView(scope.key, view.key);
          }}
          title={`Open ${scope.displayLabel} ${view.label}.`}
        >
          <SemanticIconCue icon={iconForView(view.key)} size="xs" />
          <span>{shortTabLabel(view)}</span>
        </a>
      ))}
    </nav>
  );
}

function shortTabLabel(view: DashboardViewDefinition): string {
  if (view.key === 'infographics') return 'Info';
  return view.label;
}

function shortTitle(value: string, maxLength: number): string {
  const normalized = value.replace(/\s+/g, ' ').trim();
  if (normalized.length <= maxLength) return normalized;
  return `${normalized.slice(0, Math.max(0, maxLength - 3)).trimEnd()}...`;
}

function formatLabel(value: string | null | undefined): string {
  if (!value) return '-';

  return value
    .replace(/[_-]+/g, ' ')
    .trim()
    .replace(/\w\S*/g, word => `${word.charAt(0).toLocaleUpperCase()}${word.slice(1)}`);
}

function formatGeneratedAtLabel(value: string | null): string {
  if (!value) return 'loading';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;

  return `${formatMaybeDate(value)} (${formatAgeSince(date)} ago)`;
}

function formatAgeSince(date: Date): string {
  const ageSeconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (ageSeconds < 60) return `${ageSeconds}s`;

  const ageMinutes = Math.round(ageSeconds / 60);
  if (ageMinutes < 60) return `${ageMinutes}m`;

  const ageHours = Math.round(ageMinutes / 60);
  if (ageHours < 48) return `${ageHours}h`;

  return `${Math.round(ageHours / 24)}d`;
}

function possibleActionApplyLabel(
  action: HomePossibleAction,
  state: HomePossibleActionApplyState,
): string {
  if (state.status === 'pending') return 'Applying';
  if (isApplySuccess(state.response?.status)) return 'Applied';
  if (state.status === 'error') return 'Retry';
  if (state.status === 'done') return 'Retry';
  return action.applyLabel;
}

function isApplySuccess(status: string | null | undefined): boolean {
  return status === 'applied' || status === 'already_satisfied';
}

function appliedActionFromPossibleAction(
  action: HomePossibleAction,
  response: AdviceApplyResponse,
): HomeAppliedAction {
  const recordedAt = new Date().toISOString();
  return {
    actionIndex: action.actionIndex,
    adviceId: action.adviceId,
    iconKey: action.iconKey,
    key: appliedActionKey(action.adviceId, action.actionIndex),
    ministerScope: action.ministerScope,
    priority: action.priority,
    recordedAt,
    recordedAtMs: Date.parse(recordedAt),
    resultMessage: response.message,
    resultStatus: response.status,
    title: action.title,
    tooltip: tooltipText(action.title, action.priority, response.message),
  };
}

function appliedActionsFromAdvice(advice: AdviceItem[], liveMinisters: ScopeConfig[]): HomeAppliedAction[] {
  return advice.flatMap(item => {
    const ministerScope = liveMinisters.find(scope => isScopeMinister(item.minister, scope));
    if (!ministerScope) return [];

    return item.actions.flatMap((action, index) => {
      const result = action.apply_result ?? null;
      if (!result || !isApplySuccess(result.status)) return [];

      const recordedAtMs = Date.parse(result.recorded_at);
      if (Number.isNaN(recordedAtMs)) return [];

      const display = actionDisplay(action, item);
      const detail = [
        result.message,
        action.instruction,
        action.apply?.target_summary ? `Apply: ${action.apply.target_summary}` : null,
      ].filter((value): value is string => Boolean(value)).join(' | ');

      return [{
        actionIndex: index,
        adviceId: item.id,
        iconKey: display.iconKey ?? action.kind,
        key: appliedActionKey(item.id, index),
        ministerScope,
        priority: item.priority,
        recordedAt: result.recorded_at,
        recordedAtMs,
        resultMessage: result.message,
        resultStatus: result.status,
        title: display.title,
        tooltip: tooltipText(display.title, item.priority, detail),
      }];
    });
  });
}

function appliedActionKey(adviceId: string, actionIndex: number): string {
  return `${adviceId}:applied:${actionIndex}`;
}

function mergeAppliedActionHistory(
  current: Record<string, HomeAppliedAction>,
  additions: HomeAppliedAction[],
): Record<string, HomeAppliedAction> {
  let changed = false;
  const next = { ...current };

  for (const action of additions) {
    const existing = next[action.key];
    if (!existing || existing.recordedAt !== action.recordedAt || existing.resultStatus !== action.resultStatus) {
      next[action.key] = action;
      changed = true;
    }
  }

  return changed ? pruneAppliedActionHistory(next) : current;
}

function pruneAppliedActionHistory(history: Record<string, HomeAppliedAction>): Record<string, HomeAppliedAction> {
  const entries = Object.values(history)
    .sort((left, right) => right.recordedAtMs - left.recordedAtMs)
    .slice(0, AppliedActionHistoryLimit);

  return entries.reduce<Record<string, HomeAppliedAction>>((next, action) => {
    next[action.key] = action;
    return next;
  }, {});
}

function loadAppliedActionHistory(): Record<string, HomeAppliedAction> {
  if (typeof window === 'undefined') return {};

  try {
    const raw = window.localStorage.getItem(AppliedActionHistoryStorageKey);
    if (!raw) return {};

    const parsed = JSON.parse(raw) as Record<string, HomeAppliedAction>;
    const validEntries = Object.values(parsed).filter(isValidAppliedActionRecord);
    return pruneAppliedActionHistory(validEntries.reduce<Record<string, HomeAppliedAction>>((next, action) => {
      next[action.key] = action;
      return next;
    }, {}));
  } catch {
    return {};
  }
}

function saveAppliedActionHistory(history: Record<string, HomeAppliedAction>): void {
  if (typeof window === 'undefined') return;

  try {
    window.localStorage.setItem(AppliedActionHistoryStorageKey, JSON.stringify(pruneAppliedActionHistory(history)));
  } catch {
    // Best-effort UI history only; losing it must not affect Assisted Apply behavior.
  }
}

function isValidAppliedActionRecord(value: HomeAppliedAction): boolean {
  return typeof value?.key === 'string'
    && typeof value.adviceId === 'string'
    && typeof value.actionIndex === 'number'
    && typeof value.recordedAt === 'string'
    && Number.isFinite(value.recordedAtMs)
    && typeof value.resultMessage === 'string'
    && typeof value.resultStatus === 'string'
    && typeof value.title === 'string'
    && Boolean(value.ministerScope?.key);
}

function latestAdviceIssuedAt(advice: AdviceItem[]): Date | null {
  let latestTime = Number.NEGATIVE_INFINITY;

  for (const item of advice) {
    const issuedAt = Date.parse(item.stamp.issued_at);
    if (!Number.isNaN(issuedAt) && issuedAt > latestTime) {
      latestTime = issuedAt;
    }
  }

  return Number.isFinite(latestTime) ? new Date(latestTime) : null;
}

function filteredAppliedActions(
  actions: HomeAppliedAction[],
  filter: HomeAppliedActionFilter,
  latestRegenAt: Date | null,
): HomeAppliedAction[] {
  if (filter === 'all') return actions;

  const threshold = filter === 'day'
    ? Date.now() - 24 * 60 * 60 * 1000
    : latestRegenAt?.getTime() ?? Number.POSITIVE_INFINITY;

  return actions.filter(action => action.recordedAtMs >= threshold);
}

function appliedActionFilterLabel(filter: HomeAppliedActionFilter): string {
  if (filter === 'day') return '1 day';
  if (filter === 'fresh') return 'Fresh';
  return 'All';
}

function appliedActionFilterTitle(filter: HomeAppliedActionFilter, latestRegenAt: Date | null): string {
  if (filter === 'day') return 'Show actions applied in the last 24 hours.';
  if (filter === 'fresh') {
    return latestRegenAt
      ? `Show actions applied since latest advice regeneration: ${formatMaybeDate(latestRegenAt.toISOString())}.`
      : 'Show actions applied since latest advice regeneration.';
  }

  return 'Show all applied actions observed by this dashboard session.';
}

function possibleApplyActions(
  advice: AdviceItem[],
  liveMinisters: ScopeConfig[],
  currentGameTick: number | null,
): HomePossibleAction[] {
  return advice
    .flatMap(item => {
      const ministerScope = liveMinisters.find(scope => isScopeMinister(item.minister, scope));
      if (!ministerScope) return [];

      return item.actions.flatMap((action, index) => {
        if (!isApplyReadyNow(item, action, currentGameTick)) return [];

        const display = actionDisplay(action, item);
        const detail = [
          `Minister: ${ministerScope.displayLabel}`,
          action.apply.label,
          action.apply.target_summary,
          action.instruction,
        ].filter((value): value is string => Boolean(value)).join(' | ');

        return [{
          actionIndex: index,
          adviceId: item.id,
          applyLabel: action.apply.label,
          applyTarget: action.apply.target_summary,
          iconKey: display.iconKey ?? action.kind,
          key: `${item.id}:possible:${index}`,
          ministerScope,
          priority: item.priority,
          title: display.title,
          tooltip: tooltipText(display.title, item.priority, detail),
        }];
      });
    })
    .sort((left, right) =>
      priorityRank(right.priority) - priorityRank(left.priority)
      || left.ministerScope.displayLabel.localeCompare(right.ministerScope.displayLabel)
      || left.title.localeCompare(right.title)
    );
}

function isApplyReadyNow(
  item: AdviceItem,
  action: AdviceAction,
  currentGameTick: number | null,
): action is AdviceAction & { apply: NonNullable<AdviceAction['apply']> } {
  const persistedResult = action.apply_result ?? null;
  if (persistedResult?.status === 'applied' || persistedResult?.status === 'already_satisfied') return false;
  if (adviceExpiryState(item, currentGameTick).expired) return false;
  if (!action.apply) return false;

  return isExecutableApply(action.apply);
}

function activeAdviceForScope(scope: ScopeConfig, advice: AdviceItem[]): AdviceItem[] {
  return advice
    .filter(item => isScopeMinister(item.minister, scope))
    .sort((left, right) => priorityRank(right.priority) - priorityRank(left.priority));
}

function activeFlagsForScope(scope: ScopeConfig, flags: Record<string, AgentFlag[]>): AgentFlag[] {
  return Object.values(flags)
    .flat()
    .filter(flag => isScopeMinister(flag.source_minister, scope))
    .sort((left, right) => priorityRank(right.priority) - priorityRank(left.priority));
}

function ministerOutputSections(
  advice: AdviceItem[],
  flags: AgentFlag[],
  currentGameTick: number | null,
): HomeOutputSection[] {
  return [
    outputSection('actions', 'Actions', 'actions', emittedActionItems(advice, currentGameTick)),
    outputSection('suggested_actions', 'Suggested', 'suggested_actions', suggestedActionItems(advice)),
    outputSection('building_requests', 'Build reqs', 'building_requests', buildingRequestItems(flags)),
    outputSection('labor_requests', 'Labor reqs', 'labor_requests', laborRequestItems(flags)),
    outputSection('item_requests', 'Item reqs', 'item_requests', itemRequestItems(flags)),
    outputSection('zone_requests', 'Zone reqs', 'zone_requests', zoneRequestItems(flags)),
    outputSection('attention', 'Attention', 'attention', attentionRequestItems(flags)),
  ].filter(section => section.items.length > 0);
}

function outputSection(key: string, label: string, iconKey: string, items: HomeOutputItem[]): HomeOutputSection {
  return {
    iconKey,
    items,
    key,
    label,
  };
}

function emittedActionItems(advice: AdviceItem[], currentGameTick: number | null): HomeOutputItem[] {
  return advice.flatMap(item =>
    item.actions.map((action, index) => actionItem(item, action, index, currentGameTick))
  );
}

function actionItem(
  item: AdviceItem,
  action: AdviceAction,
  index: number,
  currentGameTick: number | null,
): HomeOutputItem {
  const display = actionDisplay(action, item);
  const detail = [
    action.instruction,
    action.quantity !== null && action.quantity !== undefined ? `Qty: ${formatInteger(action.quantity)}` : null,
    action.owner ? `Owner: ${action.owner}` : null,
    action.work_type ? `Work: ${action.work_type}` : null,
    action.skill ? `Skill: ${action.skill}` : null,
    action.apply?.target_summary ? `Apply: ${action.apply.target_summary}` : null,
  ].filter((value): value is string => Boolean(value)).join(' | ');

  return {
    applyStatus: actionApplyStatus(item, action, currentGameTick),
    iconKey: display.iconKey ?? action.kind,
    key: `${item.id}:action:${index}`,
    priority: item.priority,
    title: display.title,
    tooltip: tooltipText(display.title, item.priority, detail),
  };
}

function actionApplyStatus(
  item: AdviceItem,
  action: AdviceAction,
  currentGameTick: number | null,
): HomeActionApplyStatus {
  const persistedResult = action.apply_result ?? null;
  const expiry = adviceExpiryState(item, currentGameTick);

  if (persistedResult?.status === 'applied' || persistedResult?.status === 'already_satisfied') {
    return {
      ariaLabel: 'Already applied',
      label: '✓',
      tone: 'applied',
      tooltip: formatApplyResultText(persistedResult.status, persistedResult.message),
    };
  }

  if (expiry.expired) {
    return {
      ariaLabel: 'Apply blocked',
      label: '!',
      tone: 'blocked',
      tooltip: expiry.message ?? 'Advice is expired; rerun the minister before applying.',
    };
  }

  if (!action.apply) {
    return {
      ariaLabel: 'Apply unavailable',
      label: '!',
      tone: 'blocked',
      tooltip: 'No Assisted Apply payload is attached; follow the instruction manually.',
    };
  }

  if (!isExecutableApply(action.apply)) {
    return {
      ariaLabel: 'Apply routed to Build Queue',
      label: 'BQ',
      tone: 'queued',
      tooltip: `Apply from Build Queue: ${action.apply.target_summary}`,
    };
  }

  return {
    ariaLabel: 'Apply available',
    label: '✓',
    tone: 'available',
    tooltip: `Apply available: ${action.apply.label}. ${action.apply.target_summary}`,
  };
}

function actionDisplay(action: AdviceAction, item: AdviceItem): HomeActionDisplay {
  const normalizedKind = action.kind.trim().toLowerCase();

  if (normalizedKind === 'place_blueprint' || normalizedKind === 'place_blueprint_group') {
    const target = buildActionTarget(action, item);
    return {
      iconKey: target.iconKey,
      title: `Build ${target.title}`,
    };
  }

  if (normalizedKind === 'mark_harvest' || normalizedKind === 'mark_harvest_area') {
    const target = harvestActionTarget(action);
    return {
      iconKey: target.iconKey,
      title: `Harvest ${target.title}`,
    };
  }

  if (normalizedKind === 'mark_hunt' || normalizedKind === 'mark_hunt_area') {
    const target = huntActionTarget(action);
    return {
      iconKey: target.iconKey,
      title: `Hunt ${target.title}`,
    };
  }

  if (normalizedKind === 'designate_zone') {
    const target = zoneActionTarget(action);
    return {
      iconKey: target.iconKey,
      title: `Grow ${target.title}`,
    };
  }

  if (normalizedKind === 'unforbid' || normalizedKind === 'unforbid_things') {
    return {
      iconKey: 'meal_survival_pack',
      title: unforbidActionTitle(action),
    };
  }

  return {
    iconKey: action.kind,
    title: formatLabel(action.kind),
  };
}

function buildActionTarget(action: AdviceAction, item: AdviceItem): HomeActionDisplay {
  const haystack = actionSearchText(action);

  if (haystack.includes('freezer') || haystack.includes('cooler')) {
    return {
      iconKey: 'freezer',
      title: 'Freezer',
    };
  }

  if (isButcherTableBuild(haystack)) {
    return {
      iconKey: 'table_butcher',
      title: 'Butcher Table',
    };
  }

  if (haystack.includes('campfire') && haystack.includes('stove')) {
    return {
      iconKey: 'campfire',
      title: withAdviceSource('Campfire/Stove', item),
    };
  }

  if (haystack.includes('campfire')) {
    return {
      iconKey: 'campfire',
      title: withAdviceSource('Campfire', item),
    };
  }

  if (haystack.includes('stove')) {
    return {
      iconKey: 'stove',
      title: withAdviceSource('Stove', item),
    };
  }

  if (haystack.includes('cooking') || haystack.includes('kitchen')) {
    return {
      iconKey: 'kitchen',
      title: withAdviceSource('Kitchen', item),
    };
  }

  if (haystack.includes('bed')) {
    return {
      iconKey: 'bed',
      title: 'Beds',
    };
  }

  if (haystack.includes('barracks')) {
    return {
      iconKey: 'barracks',
      title: 'Barracks',
    };
  }

  const applyTarget = conciseTarget(action.apply?.target_summary);
  if (applyTarget) {
    return {
      iconKey: action.kind,
      title: titleCaseTarget(applyTarget),
    };
  }

  const instruction = action.instruction.trim();
  const buildMatch = instruction.match(/^(?:Plan|Place|Build)\s+(?:an?|the)?\s*(.+?)(?:\s+so\b|\s+for\b|;|\.|$)/i);
  if (buildMatch?.[1]) {
    return {
      iconKey: action.kind,
      title: titleCaseTarget(normalizeBuildTarget(buildMatch[1])),
    };
  }

  const askMatch = instruction.match(/^Ask\s+\S+\s+for\s+(.+?)(?:\.|;|$)/i);
  if (askMatch?.[1]) {
    return {
      iconKey: action.kind,
      title: titleCaseTarget(normalizeBuildTarget(askMatch[1])),
    };
  }

  return {
    iconKey: action.kind,
    title: 'Request',
  };
}

function withAdviceSource(title: string, item: AdviceItem): string {
  const source = buildAdviceSource(item);
  return source ? `${title} (${source})` : title;
}

function isButcherTableBuild(haystack: string): boolean {
  return haystack.includes('butcher table')
    || haystack.includes('butcher bench')
    || haystack.includes('butchertable')
    || haystack.includes('tablebutcher');
}

function buildAdviceSource(item: AdviceItem): string | null {
  const haystack = `${item.id} ${item.title}`.toLowerCase();
  if (haystack.includes('forage') || haystack.includes('wild_harvest')) return 'Forage';
  if (haystack.includes('grow') || haystack.includes('growing')) return 'Grow';
  if (haystack.includes('hunt') || haystack.includes('animal')) return 'Hunt';
  if (haystack.includes('freezer') || haystack.includes('storage')) return 'Storage';
  return null;
}

function zoneActionTarget(action: AdviceAction): HomeActionDisplay {
  if (action.apply?.kind === 'create_growing_zone') {
    return {
      iconKey: action.apply.plant_def.toLowerCase(),
      title: `${quantityPrefix(action)}${plantDefLabel(action.apply.plant_def)}`,
    };
  }

  return {
    iconKey: 'plant_rice',
    title: `${quantityPrefix(action)}Rice`,
  };
}

function harvestActionTarget(action: AdviceAction): HomeActionDisplay {
  const haystack = actionSearchText(action);

  if (haystack.includes('plant_berry') || haystack.includes('berry')) {
    return {
      iconKey: 'plant_berry',
      title: `${quantityPrefix(action)}Berries`,
    };
  }

  const applyTarget = conciseTarget(action.apply?.target_summary);
  if (applyTarget) {
    return {
      iconKey: action.kind,
      title: titleCaseTarget(applyTarget),
    };
  }

  const instruction = action.instruction.trim();
  const harvestMatch = instruction.match(/^Mark\s+(?:up to\s+)?(.+?)\s+for harvest\b/i);
  if (harvestMatch?.[1]) {
    return {
      iconKey: action.kind,
      title: titleCaseTarget(conciseTarget(harvestMatch[1]) ?? 'Target'),
    };
  }

  return {
    iconKey: action.kind,
    title: 'Target',
  };
}

function huntActionTarget(action: AdviceAction): HomeActionDisplay {
  const haystack = actionSearchText(action);

  if (haystack.includes('tortoise')) {
    return {
      iconKey: 'mark_hunt',
      title: `${quantityPrefix(action)}tortoise (Safe)`,
    };
  }

  return {
    iconKey: 'mark_hunt',
    title: `${quantityPrefix(action)}animals`,
  };
}

function quantityPrefix(action: AdviceAction): string {
  const quantity = actionQuantity(action);
  return quantity === null ? '' : `${formatInteger(quantity)} `;
}

function actionQuantity(action: AdviceAction): number | null {
  if (action.apply?.kind === 'mark_harvest_area') return action.apply.target_count;
  if (action.apply?.kind === 'mark_hunt_area') return action.apply.target_count;
  if (action.apply?.kind === 'upsert_production_bill') return action.apply.target_count;
  if (action.apply?.kind === 'create_growing_zone') return action.apply.target_count;
  if (action.quantity !== null && action.quantity !== undefined) return action.quantity;

  return firstNumber(action.apply?.target_summary) ?? firstNumber(action.instruction);
}

function unforbidActionTitle(action: AdviceAction): string {
  const haystack = actionSearchText(action);
  const quantity = firstNumber(action.apply?.target_summary) ?? firstNumber(action.instruction);

  if (haystack.includes('packaged survival') || haystack.includes('mealsurvivalpack')) {
    return `Unforbid ${quantity === null ? '' : `${formatInteger(quantity)} `}packaged survival meals`;
  }

  return `Unforbid ${quantity === null ? '' : `${formatInteger(quantity)} `}Meals`;
}

function actionSearchText(action: AdviceAction): string {
  return [
    action.kind,
    action.instruction,
    action.apply?.target_summary,
    action.owner,
    action.work_type,
    action.skill,
  ].filter((value): value is string => Boolean(value)).join(' ').toLowerCase();
}

function normalizeBuildTarget(value: string): string {
  const target = conciseTarget(value)?.replace(/\s+build$/i, '').trim();
  return target && target.length > 0 ? target : 'request';
}

function conciseTarget(value: string | null | undefined): string | null {
  if (!value) return null;
  const target = value.replace(/\s+/g, ' ').trim();
  return target.length > 0 ? target : null;
}

function titleCaseTarget(value: string): string {
  const compactValue = value
    .replace(/\([^)]*\)/g, ' ')
    .replace(/\b\d+\s+of\s+\d+\b/gi, ' ')
    .replace(/\b\d+\b/g, ' ')
    .replace(/[_-]+/g, ' ')
    .replace(/\bplants?\b/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim();

  if (compactValue.length === 0) return 'Target';

  return compactValue
    .split(' ')
    .map(word => word.length <= 2 ? word.toUpperCase() : `${word[0].toUpperCase()}${word.slice(1).toLowerCase()}`)
    .join(' ');
}

function firstNumber(value: string | null | undefined): number | null {
  if (!value) return null;
  const match = value.match(/\b\d+\b/);
  if (!match) return null;

  const parsed = Number.parseInt(match[0], 10);
  return Number.isNaN(parsed) ? null : parsed;
}

function isExecutableApply(apply: NonNullable<AdviceAction['apply']>): boolean {
  return apply.kind !== 'place_blueprint_group';
}

function adviceExpiryState(item: AdviceItem, currentGameTick: number | null): HomeAdviceExpiryState {
  if (typeof item.stamp.expires_game_tick === 'number') {
    if (typeof currentGameTick === 'number' && item.stamp.expires_game_tick <= currentGameTick) {
      return {
        expired: true,
        message: `Expired at game tick ${formatInteger(item.stamp.expires_game_tick)}.`,
      };
    }

    return { expired: false, message: null };
  }

  const expiresAt = Date.parse(item.stamp.expires_at);
  if (!Number.isNaN(expiresAt) && expiresAt <= Date.now()) {
    return {
      expired: true,
      message: 'Expired by wall-clock TTL.',
    };
  }

  return { expired: false, message: null };
}

function formatApplyResultText(status: string, message: string): string {
  if (status === 'applied') return `Applied: ${message}`;
  if (status === 'already_satisfied') return `Already satisfied: ${message}`;
  return `${formatLabel(status)}: ${message}`;
}

function suggestedActionItems(advice: AdviceItem[]): HomeOutputItem[] {
  return advice.flatMap(item =>
    (item.suggested_actions ?? []).map((action, index) => suggestedActionItem(item, action, index))
  );
}

function suggestedActionItem(item: AdviceItem, action: SuggestedAction, index: number): HomeOutputItem {
  const title = formatLabel(action.kind);
  return {
    iconKey: action.kind,
    key: `${item.id}:suggested:${index}`,
    priority: item.priority,
    title,
    tooltip: tooltipText(title, item.priority, action.instruction),
  };
}

function buildingRequestItems(flags: AgentFlag[]): HomeOutputItem[] {
  return flags.flatMap(flag =>
    (flag.building_requests ?? []).map((request, index) => buildingRequestItem(flag, request, index))
  );
}

function buildingRequestItem(flag: AgentFlag, request: BuildingRequest, index: number): HomeOutputItem {
  const title = request.request;
  const priority = request.priority ?? flag.priority;
  const detail = [
    request.reason,
    request.target_class ? `Class: ${formatLabel(request.target_class)}` : null,
    request.target_def ? `Def: ${request.target_def}` : null,
    request.room_class ? `Room: ${formatLabel(request.room_class)}` : null,
    request.quantity !== null && request.quantity !== undefined ? `Qty: ${formatInteger(request.quantity)}` : null,
    request.requested_from ? `Owner: ${request.requested_from}` : null,
  ].filter((value): value is string => Boolean(value)).join(' | ');

  return {
    iconKey: request.target_def ?? request.target_class ?? 'building_requests',
    key: `${flag.id}:building:${index}`,
    priority,
    title,
    tooltip: tooltipText(title, priority, detail),
  };
}

function laborRequestItems(flags: AgentFlag[]): HomeOutputItem[] {
  return flags.flatMap(flag =>
    (flag.labor_requests ?? []).map((request, index) => laborRequestItem(flag, request, index))
  );
}

function laborRequestItem(flag: AgentFlag, request: LaborRequest, index: number): HomeOutputItem {
  const title = laborWorkTypeTitle(request.work_type) ?? request.request;
  const priority = request.priority ?? flag.priority;
  const detail = [
    request.request !== title ? request.request : null,
    request.reason,
    request.work_type ? `Work: ${request.work_type}` : null,
    request.skill ? `Skill: ${request.skill}` : null,
    request.quantity !== null && request.quantity !== undefined ? `Qty: ${formatInteger(request.quantity)}` : null,
    request.requested_from ? `Owner: ${request.requested_from}` : null,
  ].filter((value): value is string => Boolean(value)).join(' | ');

  return {
    iconKey: 'labor_requests',
    key: `${flag.id}:labor:${index}`,
    priority,
    title,
    tooltip: tooltipText(title, priority, detail),
  };
}

function laborWorkTypeTitle(workType: string | null | undefined): string | null {
  const normalized = workType?.trim();
  if (!normalized) return null;
  if (normalized.toLowerCase() === 'plant_cut') return 'PlantCut';

  return formatLabel(normalized);
}

function itemRequestItems(flags: AgentFlag[]): HomeOutputItem[] {
  return flags.flatMap(flag =>
    (flag.item_requests ?? []).map((request, index) => itemRequestItem(flag, request, index))
  );
}

function itemRequestItem(flag: AgentFlag, request: ItemRequest, index: number): HomeOutputItem {
  const title = itemRequestTitle(request);
  const priority = request.priority ?? flag.priority;
  const detail = [
    request.reason,
    request.item_def ? `Item: ${request.item_def}` : null,
    request.quantity !== null && request.quantity !== undefined ? `Qty: ${formatInteger(request.quantity)}` : null,
    request.requested_from ? `Owner: ${request.requested_from}` : null,
  ].filter((value): value is string => Boolean(value)).join(' | ');

  return {
    iconKey: request.item_def ?? 'item_requests',
    key: `${flag.id}:item:${index}`,
    priority,
    title,
    tooltip: tooltipText(title, priority, detail),
  };
}

function itemRequestTitle(request: ItemRequest): string {
  const haystack = [
    request.request,
    request.item_def,
    request.reason,
  ].filter((value): value is string => Boolean(value)).join(' ').toLowerCase();

  if (haystack.includes('packaged survival') || haystack.includes('mealsurvivalpack')) {
    const quantity = request.quantity !== null && request.quantity !== undefined
      ? `${formatInteger(request.quantity)} `
      : '';
    return `${quantity}forbidden packaged survival meals`;
  }

  return request.request;
}

function zoneRequestItems(flags: AgentFlag[]): HomeOutputItem[] {
  return flags.flatMap(flag =>
    (flag.zone_requests ?? []).map((request, index) => zoneRequestItem(flag, request, index))
  );
}

function zoneRequestItem(flag: AgentFlag, request: ZoneRequest, index: number): HomeOutputItem {
  const title = zoneRequestTitle(request);
  const priority = request.priority ?? flag.priority;
  const detail = [
    request.request !== title ? request.request : null,
    request.reason,
    request.zone_class ? `Zone: ${formatLabel(request.zone_class)}` : null,
    request.plant_def ? `Plant: ${request.plant_def}` : null,
    request.tile_count !== null && request.tile_count !== undefined ? `Tiles: ${formatInteger(request.tile_count)}` : null,
    request.terrain?.preferred_fertility !== null && request.terrain?.preferred_fertility !== undefined
      ? `Fertility: ${request.terrain.preferred_fertility.toLocaleString(undefined, { maximumFractionDigits: 2 })}`
      : null,
    request.requested_from ? `Owner: ${request.requested_from}` : null,
  ].filter((value): value is string => Boolean(value)).join(' | ');

  return {
    iconKey: request.plant_def ?? 'zone_requests',
    key: `${flag.id}:zone:${index}`,
    priority,
    title,
    tooltip: tooltipText(title, priority, detail),
  };
}

function zoneRequestTitle(request: ZoneRequest): string {
  const plant = request.plant_def ? plantDefLabel(request.plant_def) : formatLabel(request.zone_class);
  const tiles = request.tile_count !== null && request.tile_count !== undefined
    ? `${formatInteger(request.tile_count)} `
    : '';
  return `${tiles}${plant} zone`;
}

function plantDefLabel(def: string): string {
  const trimmed = def.trim().replace(/^Plant_/i, '');
  return trimmed
    .replace(/_/g, ' ')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function attentionRequestItems(flags: AgentFlag[]): HomeOutputItem[] {
  return flags.flatMap(flag =>
    (flag.attention ?? []).map((request, index) => attentionRequestItem(flag, request, index))
  );
}

function attentionRequestItem(flag: AgentFlag, request: AttentionRequest, index: number): HomeOutputItem {
  const title = request.request;
  const priority = request.priority ?? flag.priority;
  const detail = [
    request.reason,
    request.requested_from ? `Owner: ${request.requested_from}` : null,
  ].filter((value): value is string => Boolean(value)).join(' | ');

  return {
    iconKey: 'attention',
    key: `${flag.id}:attention:${index}`,
    priority,
    title,
    tooltip: tooltipText(title, priority, detail),
  };
}

function tooltipText(title: string, priority: Priority, detail: string): string {
  return [title, `Priority: ${priority}`, detail].filter(Boolean).join(' | ');
}

function bottomLineAdvice(
  scope: ScopeConfig,
  advice: AdviceItem[],
  stateSummaries: Record<string, string>,
  agenda: MayorAgenda | null,
): HomeAdviceSummary {
  if (scope.key === 'mayor') {
    const activePriority = agenda?.short_term.find(item => item.status === 'active') ?? agenda?.short_term[0] ?? null;
    return {
      icon: iconForScope(scope.key),
      title: agenda?.posture.summary ?? 'No agenda yet',
      tone: activePriority ? 'ok' : 'neutral',
    };
  }

  const topAdvice = advice[0] ?? null;
  if (topAdvice) {
    return {
      icon: iconForAdvice(scope, topAdvice),
      title: topAdvice.title,
      tone: priorityTone(topAdvice.priority),
    };
  }

  const stateSummary = valueForScope(stateSummaries, scope);
  if (stateSummary) {
    return {
      icon: iconForScope(scope.key),
      title: firstLine(stateSummary),
      tone: 'neutral',
    };
  }

  return {
    icon: iconForScope(scope.key) ?? iconForField('advice'),
    title: 'No active advice',
    tone: 'neutral',
  };
}

function iconForAdvice(scope: ScopeConfig, advice: AdviceItem): SemanticIconSpec | undefined {
  const firstActionKind = advice.actions[0]?.kind ?? advice.suggested_actions?.[0]?.kind ?? null;
  return iconForActionKind(firstActionKind) ?? iconForScope(scope.key) ?? iconForField('advice');
}

function ruleSummary(
  scope: ScopeConfig,
  health: SystemHealth | null,
  hostApiLive: boolean,
): { status: string; title: string; detail: string | null; tone: PillTone } {
  if (!health) {
    return {
      status: hostApiLive ? 'loading' : 'missing',
      title: hostApiLive ? 'Trace loading' : 'No current Host health',
      detail: hostApiLive ? null : 'Host API is not live.',
      tone: 'idle',
    };
  }

  const trace = health.traces.find(item => isScopeMinister(item.minister, scope));
  if (!trace) {
    if (!scope.canRunRules && scope.canRunLlm) {
      return {
        status: 'LLM-only',
        title: 'No trace yet',
        detail: 'Mayor is excluded from rules-only cabinet runs.',
        tone: 'info',
      };
    }

    return {
      status: scope.canRunRules ? 'no trace' : 'not wired',
      title: scope.canRunRules ? 'No trace yet' : 'Rules not wired',
      detail: null,
      tone: 'idle',
    };
  }

  const staleDetail = hostApiLive ? null : 'Host API is stale; showing the last trace.';
  const status = hostApiLive ? trace.status : `last ${trace.status}`;
  const reason = trace.ruleFired
    ? `rule ${trace.ruleFired}`
    : trace.escalationReason ?? trace.note;
  const detailParts = [
    reason,
    trace.adviceCount !== null ? `${trace.adviceCount} advice` : null,
    trace.flagCount !== null ? `${trace.flagCount} flags` : null,
    formatTraceTime(trace.completedAt ?? trace.startedAt),
    staleDetail,
  ].filter(Boolean);

  return {
    status,
    title: trace.path ? trace.path.replace(/_/g, ' ') : 'path unknown',
    detail: detailParts.join(' | '),
    tone: traceTone(trace, hostApiLive),
  };
}

function traceTone(trace: MinisterTrace, hostApiLive: boolean): PillTone {
  if (!hostApiLive) return 'idle';
  if (trace.status === 'failed' || trace.path === 'llm_failed') return 'error';
  if (trace.status === 'running' || trace.path === 'llm') return 'info';
  if (trace.path === 'rules') return 'ok';
  return 'idle';
}

function priorityRank(priority: Priority): number {
  if (priority === 'critical') return 3;
  if (priority === 'high') return 2;
  if (priority === 'medium') return 1;
  return 0;
}

function priorityTone(priority: Priority): MetricTone {
  if (priority === 'critical') return 'error';
  if (priority === 'high') return 'warn';
  if (priority === 'medium') return 'ok';
  return 'neutral';
}

function originTone(origin: string | null | undefined): MetricTone {
  if (origin === 'live') return 'ok';
  if (!origin) return 'neutral';
  return 'warn';
}

function cabinetRunTone(run: CabinetRunLogSnapshot | null): MetricTone {
  if (!run) return 'neutral';
  if (run.status === 'failed') return 'error';
  if (run.status === 'running') return 'warn';
  return 'ok';
}

function formatCabinetRun(run: CabinetRunLogSnapshot | null): string {
  if (!run) return 'none';
  const scope = run.scope === 'cabinet_rules' ? 'rules' : 'full';
  return `${scope} ${run.status.replace(/_/g, ' ')}`;
}

function activeFlagCount(flags: Record<string, AgentFlag[]>): number {
  return Object.values(flags).reduce((sum, ministerFlags) => sum + ministerFlags.length, 0);
}

function formatInteger(value: number): string {
  return value.toLocaleString();
}

function firstLine(value: string): string {
  const normalized = value.replace(/\s+/g, ' ').trim();
  if (!normalized) return 'No detail';
  return normalized.length > 150 ? `${normalized.slice(0, 147)}...` : normalized;
}

function formatTraceTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}
