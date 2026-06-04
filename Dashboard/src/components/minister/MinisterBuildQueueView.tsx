import { useState, type ReactNode } from 'react';
import { applyAdviceAction } from '../../api/advice';
import { iconUrlFor } from '../../api/icons';
import { fetchBriefing } from '../../api/ministers';
import { displayMinisterName, type ScopeConfig } from '../../dashboard/scopes';
import { iconForActionKind, iconForField, iconForView } from '../../dashboard/semanticIcons';
import { isScopeMinister } from '../../dashboard/selectors';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type {
  AdviceAction,
  AdviceApplyResponse,
  AdviceItem,
  AdviceOption,
  AgentFlag,
  BlueprintGroup,
  BuildingRequest,
} from '../../types/advice';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { IconizedText } from '../shared/IconizedText';
import { DynamicTable } from '../shared/Inspector';
import { ResourceQuantity, ResourceQuantityList } from '../shared/ResourceQuantity';
import { StatusPill, type PillTone } from '../shared/StatusPill';
import { SemanticLabel } from '../shared/SemanticIcon';
import { BlueprintFootprintThumbnail } from './BlueprintFootprintThumbnail';
import { readinessTone } from './readiness';

export function MinisterBuildQueueView({
  advice,
  currentGameTick,
  flags,
  scope,
}: {
  advice: AdviceItem[];
  currentGameTick: number | null;
  flags: Record<string, AgentFlag[]>;
  scope: ScopeConfig;
}) {
  const briefing = useAsyncResource(signal => fetchBriefing(scope.key, signal), [scope.key]);
  const requests = buildRequestCards(flags);
  const proposedOptionGroups = buildProposedOptionGroups(advice, scope, currentGameTick);
  const proposedOptionCount = proposedOptionGroups.reduce((sum, group) => sum + group.options.length, 0);
  const backlog = buildBacklogModel(briefing.data);
  const anySectionHasRows = requests.length > 0 || proposedOptionCount > 0 || backlog.groups.length > 0 || backlog.pending > 0;

  if (scope.key !== 'willie') {
    return <EmptyState code="BUILD QUEUE NOT WIRED">Build Queue is a Willie-only construction view.</EmptyState>;
  }

  return (
    <div className="minister-view build-queue-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.displayLabel}</span>
        <h2><SemanticLabel icon={iconForView('build_queue')}><span>Build Queue</span></SemanticLabel></h2>
        <p>Requested builds, solver proposals, and placed blueprints or frames in one inspection surface.</p>
      </header>

      {!anySectionHasRows && !briefing.loading && !briefing.error && (
        <EmptyState code="NO BUILD QUEUE ITEMS">Willie has no requested builds, placement options, or visible backlog rows right now.</EmptyState>
      )}

      <div className="build-queue-sections" aria-label="Willie build queue sections">
        <BuildQueueSection title="Requested" count={requests.length} iconKey="building_requests">
          {requests.length === 0 ? (
            <LaneEmpty>Building requests aimed at Willie will appear here.</LaneEmpty>
          ) : (
            <div className="build-request-stack">
              {requests.map(card => <RequestCard card={card} key={card.id} />)}
            </div>
          )}
        </BuildQueueSection>

        <BuildQueueSection
          title="Proposed"
          count={proposedOptionCount}
          iconKey="place_blueprint"
          meta={proposedOptionGroups.length > 0 ? `${formatInteger(proposedOptionGroups.length)} ${proposedOptionGroups.length === 1 ? 'issue' : 'issues'}` : undefined}
        >
          {proposedOptionCount === 0 ? (
            <LaneEmpty>{proposedEmptyHint(requests.length)}</LaneEmpty>
          ) : (
            <div className="build-option-group-stack">
              {proposedOptionGroups.map(group => <OptionGroup group={group} key={group.item.id} />)}
            </div>
          )}
        </BuildQueueSection>

        <BuildQueueSection
          title="Placed"
          count={backlog.groups.length}
          iconKey="construction_backlog"
          meta={briefing.loading ? 'loading' : `${formatInteger(backlog.pending)} pending / ${formatInteger(backlog.blocked)} blocked`}
        >
          {briefing.loading ? (
            <LaneEmpty>Loading current placed build evidence.</LaneEmpty>
          ) : briefing.error ? (
            <LaneEmpty>{briefing.error}</LaneEmpty>
          ) : (
            <BacklogLane backlog={backlog} />
          )}
        </BuildQueueSection>
      </div>
    </div>
  );
}

type RequestCardModel = {
  flag: AgentFlag;
  id: string;
  request: BuildingRequest;
};

type RequestDetailChip = {
  content: ReactNode;
  key: string;
};

type ProposedOptionModel = {
  actionMatch: OptionActionMatch | null;
  expired: AdviceExpiryState;
  item: AdviceItem;
  option: AdviceOption;
};

type ProposedOptionGroupModel = {
  expired: AdviceExpiryState;
  item: AdviceItem;
  options: ProposedOptionModel[];
};

type OptionActionMatch = {
  action: AdviceAction;
  actionIndex: number;
};

type BacklogModel = {
  blocked: number;
  disallowed: number;
  groups: unknown[];
  pending: number;
};

function BuildQueueSection({
  children,
  count,
  defaultOpen = true,
  iconKey,
  meta,
  title,
}: {
  children: ReactNode;
  count: number;
  defaultOpen?: boolean;
  iconKey: string;
  meta?: string;
  title: string;
}) {
  const countText = `${formatInteger(count)} ${count === 1 ? 'item' : 'items'}`;

  return (
    <DisclosureSection
      defaultOpen={defaultOpen}
      meta={meta ? `${countText} / ${meta}` : countText}
      title={<SemanticLabel icon={iconForField(iconKey)}><span>{title}</span></SemanticLabel>}
    >
      {children}
    </DisclosureSection>
  );
}

function LaneEmpty({ children }: { children: ReactNode }) {
  return <p className="build-queue-empty">{children}</p>;
}

function proposedEmptyHint(requestCount: number): string {
  return requestCount === 0
    ? 'No build has been requested of Willie yet, so the placement solver has nothing to place. When another minister such as Chef requests a build it appears under Requested above, and validated placement options show up here. Willie can also self-propose when it detects a missing room.'
    : 'Willie has an open build request, but the solver has not returned a validated footprint yet. Open the driving advice on the Advice tab for the reason: missing anchor, no reachable path, or failed validation.';
}

function RequestCard({ card }: { card: RequestCardModel }) {
  const request = card.request;
  const requestIcon = iconForField(request.target_def ?? request.target_class);
  const details = formatRequestDetails(request);

  return (
    <article className="build-request-card">
      <header>
        <GameIcon
          fallback={requestIcon?.fallback ?? 'BQ'}
          label={requestIcon?.label ?? 'Build request icon'}
          size="xs"
          src={iconUrlFor(requestIcon?.ref)}
        />
        <div>
          <span className="eyebrow">{displayMinisterName(card.flag.source_minister)} to {displayMinisterName('Willie')}</span>
          <h4><IconizedText maxIcons={1} text={request.request} /></h4>
        </div>
        <StatusPill tone={priorityTone(request.priority ?? card.flag.priority)}>{request.priority ?? card.flag.priority}</StatusPill>
      </header>
      <p><IconizedText maxIcons={2} text={request.reason} /></p>
      {details.length > 0 && (
        <div className="build-request-tags">
          {details.map(detail => <span key={detail.key}>{detail.content}</span>)}
        </div>
      )}
      {card.flag.summary && <small><IconizedText maxIcons={1} text={card.flag.summary} /></small>}
    </article>
  );
}

function OptionGroup({ group }: { group: ProposedOptionGroupModel }) {
  return (
    <section className="build-option-group" aria-label={`${group.item.title} proposed options`}>
      <header>
        <div>
          <span className="eyebrow">
            <SemanticLabel icon={iconForField(group.item.title)}><span>{group.item.title}</span></SemanticLabel>
          </span>
          <h3><IconizedText maxIcons={1} text={group.item.title} /></h3>
        </div>
        <div className="build-option-group-meta">
          <StatusPill tone={priorityTone(group.item.priority)}>{group.item.priority}</StatusPill>
          <span>{formatInteger(group.options.length)} {group.options.length === 1 ? 'option' : 'options'}</span>
        </div>
      </header>
      <p><IconizedText maxIcons={2} text={group.item.body} /></p>
      {group.expired.message && <small className={group.expired.expired ? 'expired' : ''}>{group.expired.message}</small>}
      <div className="build-option-grid">
        {group.options.map(card => <OptionCard card={card} key={`${card.item.id}:${card.option.id}`} />)}
      </div>
    </section>
  );
}

function OptionCard({ card }: { card: ProposedOptionModel }) {
  const [applyState, setApplyState] = useState<ActionApplyState>({ status: 'idle', response: null, error: null });
  const actionIcon = iconForActionKind('place_blueprint');
  const success = applyState.response?.status === 'applied' || applyState.response?.status === 'already_satisfied';
  const disabled = card.actionMatch === null || card.expired.expired || applyState.status === 'pending' || success;
  const disabledReason = card.actionMatch === null
    ? 'No place_blueprint_group action payload is attached to this option yet.'
    : card.expired.message;

  const onApply = async () => {
    if (!card.actionMatch || disabled) return;
    setApplyState({ status: 'pending', response: null, error: null });
    try {
      const response = await applyAdviceAction(card.item.id, card.actionMatch.actionIndex);
      setApplyState({ status: 'done', response, error: null });
    } catch (error) {
      setApplyState({ status: 'error', response: null, error: String(error) });
    }
  };

  return (
    <article className={`build-option-card ${card.item.priority}`}>
      <header>
        <GameIcon
          fallback={actionIcon?.fallback ?? 'BP'}
          label={actionIcon?.label ?? 'Blueprint option icon'}
          size="xs"
          src={iconUrlFor(actionIcon?.ref)}
        />
        <div>
          <span className="eyebrow">{card.item.title}</span>
          <h4>{card.option.label}</h4>
        </div>
        <StatusPill tone={priorityTone(card.item.priority)}>{card.item.priority}</StatusPill>
      </header>

      <BlueprintFootprintThumbnail group={card.option.blueprint_group} />
      <p><IconizedText maxIcons={2} text={card.option.summary} /></p>

      {card.option.readiness && (
        <div className="build-option-readiness" aria-label={`${card.option.label} readiness`}>
          <StatusPill tone={readinessTone(card.option.readiness.draftable)} title="Draftable">draft {card.option.readiness.draftable}</StatusPill>
          <StatusPill tone={readinessTone(card.option.readiness.placement_valid)} title="Placement validation">place {card.option.readiness.placement_valid}</StatusPill>
          <StatusPill tone={readinessTone(card.option.readiness.materials_ready)} title="Materials ready">mat {card.option.readiness.materials_ready}</StatusPill>
          <StatusPill tone={readinessTone(card.option.readiness.apply_ready)} title="Apply ready">apply {card.option.readiness.apply_ready}</StatusPill>
        </div>
      )}

      <div className="build-option-materials">
        {card.option.est_materials.length === 0 ? (
          <span>No material estimate.</span>
        ) : (
          card.option.est_materials.map(material => (
            <ResourceQuantity defName={material.def_name} key={`${card.option.id}-${material.def_name}`} quantity={material.count} />
          ))
        )}
      </div>

      {card.option.tradeoff_note && <blockquote><IconizedText maxIcons={1} text={card.option.tradeoff_note} /></blockquote>}

      <div className="action-apply build-option-apply">
        <button
          type="button"
          disabled={disabled}
          onClick={() => void onApply()}
          title={card.actionMatch?.action.apply?.target_summary ?? disabledReason ?? 'Apply this blueprint option'}
        >
          {buttonLabel(card, applyState, success)}
        </button>
        <small className={`action-apply-result ${applyState.response?.status ?? applyState.status}`}>
          {applyState.response?.message ?? applyState.error ?? disabledReason ?? card.actionMatch?.action.apply?.target_summary}
        </small>
      </div>
    </article>
  );
}

function BacklogLane({ backlog }: { backlog: BacklogModel }) {
  if (backlog.groups.length === 0) {
    return <LaneEmpty>No placed blueprints or frames are visible.</LaneEmpty>;
  }

  return (
    <div className="build-backlog-stack">
      <div className="build-backlog-summary">
        <StatusPill tone={backlog.blocked > 0 ? 'warn' : 'idle'}>{formatInteger(backlog.blocked)} blocked</StatusPill>
        <StatusPill tone={backlog.disallowed > 0 ? 'warn' : 'idle'}>{formatInteger(backlog.disallowed)} disallowed</StatusPill>
      </div>
      <DynamicTable
        rows={backlog.groups}
        preferredColumns={['kind', 'defName', 'stuffDefName', 'allowed', 'count', 'blockedCount', 'disallowedCount', 'totalWorkLeft']}
        emptyMessage="No placed blueprints or frames are visible."
      />
    </div>
  );
}

type ActionApplyState = {
  error: string | null;
  response: AdviceApplyResponse | null;
  status: 'idle' | 'pending' | 'done' | 'error';
};

type AdviceExpiryState = {
  expired: boolean;
  message: string | null;
};

function buildRequestCards(flags: Record<string, AgentFlag[]>): RequestCardModel[] {
  return Object.values(flags)
    .flat()
    .flatMap(flag => (flag.building_requests ?? [])
      .filter(request => isWillieRequest(request))
      .map((request, index) => ({
        flag,
        id: `${flag.id}:${request.request}:${index}`,
        request,
      })));
}

function buildProposedOptionGroups(
  advice: AdviceItem[],
  scope: ScopeConfig,
  currentGameTick: number | null,
): ProposedOptionGroupModel[] {
  return advice
    .filter(item => isScopeMinister(item.minister, scope))
    .map(item => {
      const expired = adviceExpiryState(item, currentGameTick);
      const options = (item.options ?? []).map(option => ({
        actionMatch: findBlueprintAction(item, option),
        expired,
        item,
        option,
      }));

      return {
        expired,
        item,
        options,
      };
    })
    .filter(group => group.options.length > 0);
}

function buildBacklogModel(briefing: unknown): BacklogModel {
  const source = isRecord(briefing) ? briefing : null;
  const constructionBacklog = recordAt(source, 'constructionBacklog');
  const stalledBuilds = recordAt(source, 'stalledBuilds');

  return {
    blocked: numberAt(stalledBuilds, 'blockedCount') ?? 0,
    disallowed: numberAt(stalledBuilds, 'disallowedCount') ?? 0,
    groups: arrayAt(constructionBacklog, 'groups'),
    pending: numberAt(stalledBuilds, 'pendingBuildCount') ?? 0,
  };
}

function findBlueprintAction(item: AdviceItem, option: AdviceOption): OptionActionMatch | null {
  for (let actionIndex = 0; actionIndex < item.actions.length; actionIndex += 1) {
    const action = item.actions[actionIndex];
    const apply = action.apply;
    if (apply?.kind === 'place_blueprint_group' && sameBlueprintGroup(apply.blueprint_group, option.blueprint_group)) {
      return { action, actionIndex };
    }
  }

  return null;
}

function sameBlueprintGroup(left: BlueprintGroup, right: BlueprintGroup): boolean {
  return left.map_id === right.map_id &&
    (left.label === right.label || blueprintFingerprint(left) === blueprintFingerprint(right));
}

function blueprintFingerprint(group: BlueprintGroup): string {
  return group.assets
    .map(asset => `${asset.role}:${asset.def_name}:${asset.stuff_def_name ?? ''}:${asset.cell.x}:${asset.cell.z}:${asset.rotation}`)
    .sort()
    .join('|');
}

function buttonLabel(card: ProposedOptionModel, state: ActionApplyState, success: boolean): string {
  if (card.expired.expired) return 'Expired';
  if (state.status === 'pending') return 'Applying';
  if (success) return 'Applied';
  return card.actionMatch?.action.apply?.label ?? 'Apply not wired';
}

function isWillieRequest(request: BuildingRequest): boolean {
  return stringEquals(request.requested_from, 'Willie');
}

function formatRequestDetails(request: BuildingRequest): RequestDetailChip[] {
  return [
    request.target_class ? chip('target-class', `class ${formatLabel(request.target_class)}`) : null,
    request.target_def ? chip('target-def', `def ${request.target_def}`) : null,
    request.room_class ? chip('room-class', `room ${formatLabel(request.room_class)}`) : null,
    textChip('capacity', formatCapacity(request)),
    textChip('adjacency', formatAdjacency(request)),
    request.urgency ? chip('urgency', `urgency ${formatLabel(request.urgency)}`) : null,
    materialChip(request),
  ].filter((value): value is RequestDetailChip => value !== null);
}

function chip(key: string, content: ReactNode): RequestDetailChip {
  return { key, content };
}

function textChip(key: string, value: string | null): RequestDetailChip | null {
  return value ? chip(key, value) : null;
}

function formatCapacity(request: BuildingRequest): string | null {
  const capacity = request.capacity_need;
  if (!capacity) return null;

  const amount = capacity.amount === null || capacity.amount === undefined
    ? ''
    : ` ${formatInteger(capacity.amount)}`;
  const unit = capacity.unit ? ` ${capacity.unit}` : '';
  return `capacity ${formatLabel(capacity.measure)}${amount}${unit}`;
}

function formatAdjacency(request: BuildingRequest): string | null {
  if (!request.adjacency || request.adjacency.length === 0) return null;
  return `near ${request.adjacency.map(hint => `${formatLabel(hint.relation)} ${hint.target}`).join(', ')}`;
}

function materialChip(request: BuildingRequest): RequestDetailChip | null {
  if (!request.materials_on_hand || request.materials_on_hand.length === 0) return null;
  return chip(
    'materials',
    <>
      <span>materials</span>
      <ResourceQuantityList
        items={request.materials_on_hand.map(material => ({
          approx: material.approx_qty !== null && material.approx_qty !== undefined,
          defName: material.material,
          quantity: material.approx_qty,
        }))}
      />
    </>,
  );
}

function priorityTone(priority: string | null | undefined): PillTone {
  const normalized = (priority ?? '').toLowerCase();
  if (normalized === 'critical' || normalized === 'high') return 'error';
  if (normalized === 'medium') return 'warn';
  if (normalized === 'low') return 'info';
  return 'idle';
}

function adviceExpiryState(item: AdviceItem, currentGameTick: number | null): AdviceExpiryState {
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
    return { expired: true, message: 'Expired by wall-clock TTL.' };
  }

  return { expired: false, message: null };
}

function recordAt(source: Record<string, unknown> | null, key: string): Record<string, unknown> | null {
  if (!source) return null;
  const value = source[key];
  return isRecord(value) ? value : null;
}

function arrayAt(source: Record<string, unknown> | null, key: string): unknown[] {
  if (!source) return [];
  const value = source[key];
  return Array.isArray(value) ? value : [];
}

function numberAt(source: Record<string, unknown> | null, key: string): number | null {
  if (!source) return null;
  const value = source[key];
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function stringEquals(left: string | null | undefined, right: string): boolean {
  return typeof left === 'string' && left.localeCompare(right, undefined, { sensitivity: 'accent' }) === 0;
}

function formatLabel(value: string | null | undefined): string {
  if (!value) return '-';
  if (value === 'buildable_region') return 'Buildable region / Home area';
  return value
    .replace(/_/g, ' ')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function formatInteger(value: number): string {
  return Math.round(value).toLocaleString();
}
