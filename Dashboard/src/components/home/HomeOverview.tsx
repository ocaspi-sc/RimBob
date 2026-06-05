import type { ReactNode } from 'react';
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
  AdviceItem,
  AgentFlag,
  AttentionRequest,
  BuildingRequest,
  ItemRequest,
  LaborRequest,
  Priority,
  SuggestedAction,
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

type HomeAdviceExpiryState = {
  expired: boolean;
  message: string | null;
};

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
  const disableCabinetControls = cabinetBusy || !hostApiLive;

  return (
    <div className="home-overview">
      <section className="home-command-bar" aria-label="CABINET command bar">
        <div className="home-command-title">
          <span className="eyebrow">
            <SemanticIconCue icon={iconForScope('home')} size="xs" />
            CABINET
          </span>
          <h2>RimBob Command Summary</h2>
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
              currentGameTick={snapshot?.gameTick ?? null}
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

function SectionHeading({ iconKey, title }: { iconKey: string; title: string }) {
  return (
    <div className="home-section-heading">
      <h3>
        <SemanticLabel icon={iconForField(iconKey)}>
          <span>{title}</span>
        </SemanticLabel>
      </h3>
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
  if (view.key === 'build_queue') return 'Queue';
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
    return {
      iconKey: 'plant_rice',
      title: `Grow ${quantityPrefix(action)}rice`,
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
