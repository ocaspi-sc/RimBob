import { useState, type ReactNode } from 'react';
import type { MayorAgenda, AgendaPriority } from '../../types/agenda';
import { fetchTrace } from '../../api/ministers';
import type {
  AdviceActionApply,
  AdviceApplyResponse,
  AdviceItem,
  AgentFlag,
  AttentionRequest,
  BuildingRequest,
  ItemRequest,
  LaborRequest,
} from '../../types/advice';
import { displayMinisterName, type ScopeConfig } from '../../dashboard/scopes';
import { isScopeMinister } from '../../dashboard/selectors';
import { applyAdviceAction } from '../../api/advice';
import { iconUrlFor } from '../../api/icons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { DashboardEvent, MinisterTrace } from '../../types/system';
import {
  iconForActionKind,
  iconForAgendaCategory,
  iconForAgendaPriority,
  iconForField,
  iconForSection,
  iconForStateSummaryLine,
  iconForView,
} from '../../dashboard/semanticIcons';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { IconizedText } from '../shared/IconizedText';
import { ResourceQuantity, ResourceQuantityList } from '../shared/ResourceQuantity';
import { SemanticIconCue, SemanticLabel } from '../shared/SemanticIcon';
import { MinisterEscalationCallout } from './MinisterEscalationCallout';

export function MinisterAdviceView({
  advice,
  agenda,
  currentGameTick,
  events,
  flags,
  manualTriggerTarget,
  previousAgenda,
  scope,
  stateSummary,
}: {
  advice: AdviceItem[];
  agenda: MayorAgenda | null;
  currentGameTick: number | null;
  events: DashboardEvent[];
  flags: AgentFlag[];
  manualTriggerTarget: string | null;
  previousAgenda: MayorAgenda | null;
  scope: ScopeConfig;
  stateSummary: string | null;
}) {
  const ministerAdvice = advice.filter(item => isScopeMinister(item.minister, scope));
  const ministerEvents = events.filter(event => isScopeMinister(event.source, scope));
  const latestMinisterEventId = ministerEvents[0]?.id ?? 'none';
  const latestAdviceIssuedAt = ministerAdvice[0]?.stamp.issued_at ?? 'none';
  const trace = useAsyncResource(signal => fetchTrace(scope.key, signal), [
    scope.key,
    latestMinisterEventId,
    latestAdviceIssuedAt,
    manualTriggerTarget,
  ]);

  if (scope.key === 'mayor') {
    return <MayorAdvice agenda={agenda} latestTrace={trace.data} previousAgenda={previousAgenda} />;
  }

  if (scope.status !== 'live') {
    return <EmptyState code="ADVICE NOT WIRED">{scope.displayLabel} is planned and not emitting advice yet.</EmptyState>;
  }

  const hasEscalation = Boolean(trace.data?.escalationReason?.trim());

  if (ministerAdvice.length === 0 && !stateSummary && flags.length === 0 && !hasEscalation) {
    return <EmptyState code="NO ACTIVE ADVICE">{scope.displayLabel} has not emitted active advice in this session.</EmptyState>;
  }

  const currentStateLines = stateSummary ? splitStateSummary(stateSummary) : [];

  return (
    <div className="minister-view advice-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.displayLabel}</span>
        <h2><SemanticLabel icon={iconForView('advice')}><span>Advice</span></SemanticLabel></h2>
        <p>Latest feeder minister advice from the persisted SSE snapshot.</p>
      </header>
      <MinisterEscalationCallout trace={trace.data} />
      {stateSummary && (
        <section className="advice-state-summary">
          <span className="eyebrow">Current State</span>
          {currentStateLines.length > 0 ? (
            <table className="state-summary-table" aria-label={`${scope.displayLabel} current state summary`}>
              <tbody>
                {currentStateLines.map((line, index) => (
                  <tr key={`${scope.key}-state-${index}`}>
                    <th scope="row">
                      <SemanticLabel icon={iconForStateSummaryLine(line.label, line.detail) ?? iconForField(line.iconKey)}>
                        <span>{line.label ?? 'State'}</span>
                      </SemanticLabel>
                    </th>
                    <td><IconizedText maxIcons={3} text={line.detail} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <p><IconizedText maxIcons={3} text={stateSummary} /></p>
          )}
        </section>
      )}
      {ministerAdvice.length === 0 && (
        <EmptyState code="NO ADVICE ITEMS">{scope.displayLabel} has no advice items in the latest snapshot.</EmptyState>
      )}
      {flags.length > 0 && <AgentFlagsPanel flags={flags} />}
      <div className="advice-stack">
        {ministerAdvice.map(item => <AdviceCard currentGameTick={currentGameTick} key={item.id} item={item} />)}
      </div>
    </div>
  );
}

type StateSummaryLine = {
  detail: string;
  iconKey: string;
  label: string | null;
};

const STATE_SUMMARY_ICON_KEYS: Record<string, string> = {
  acquisition: 'wild_harvest',
  'build queue': 'build_queue',
  coverage: 'data_coverage',
  crops: 'crops',
  'confidence gaps': 'data_coverage',
  'kitchen/storage': 'kitchen',
  power: 'power_stability',
  rooms: 'functional_rooms',
  stores: 'food',
};

function splitStateSummary(summary: string): StateSummaryLine[] {
  return summary
    .split(/\r?\n/)
    .map(parseStateSummaryLine)
    .filter((line): line is StateSummaryLine => line !== null);
}

function parseStateSummaryLine(line: string): StateSummaryLine | null {
  const text = line.trim().replace(/^-\s*/, '');
  if (!text) return null;

  const separator = text.indexOf(':');
  if (separator <= 0) {
    return { detail: text, iconKey: 'info', label: null };
  }

  const label = text.slice(0, separator).trim();
  const detail = text.slice(separator + 1).trim();

  return {
    detail,
    iconKey: STATE_SUMMARY_ICON_KEYS[label.toLowerCase()] ?? label,
    label,
  };
}

function MayorAdvice({
  agenda,
  latestTrace,
  previousAgenda,
}: {
  agenda: MayorAgenda | null;
  latestTrace: MinisterTrace | null;
  previousAgenda: MayorAgenda | null;
}) {
  if (!agenda) {
    return (
      <div className="minister-view advice-view mayor-advice">
        <header className="view-heading">
          <span className="eyebrow">{displayMinisterName('Mayor')} Advice</span>
          <h2><SemanticLabel icon={iconForView('advice')}><span>Advice</span></SemanticLabel></h2>
          <p>Waiting for {displayMinisterName('Mayor')}'s first agenda update.</p>
        </header>
        <MinisterEscalationCallout trace={latestTrace} />
        <EmptyState code="NO AGENDA">Waiting for {displayMinisterName('Mayor')}'s first agenda update.</EmptyState>
      </div>
    );
  }

  const previousShort = new Map((previousAgenda?.short_term ?? []).map(item => [item.id, item]));
  const activeShort = agenda.short_term.filter(item => item.status === 'active');
  const closedShort = agenda.short_term.filter(item => item.status !== 'active');

  return (
    <div className="minister-view advice-view mayor-advice">
      <header className="agenda-hero">
        <div>
          <span className="eyebrow">{displayMinisterName('Mayor')} Advice</span>
          <h2><IconizedText maxIcons={3} text={agenda.posture.summary} /></h2>
        </div>
        <div className="agenda-stamps">
          <span>v{agenda.version}</span>
          <span>{agenda.updated_game_date.label}</span>
          <span>{agenda.updated_game_date.totalDays.toFixed(1)} total days</span>
          <span>{formatTime(agenda.generated_at)}</span>
        </div>
      </header>
      <MinisterEscalationCallout trace={latestTrace} />

      <section className="posture-band">
        <span><IconizedText maxIcons={3} text={agenda.posture.economic} /></span>
        <span><IconizedText maxIcons={3} text={agenda.posture.military} /></span>
      </section>

      <DisclosureSection
        title={<SemanticLabel icon={iconForSection('state_of_the_union')}><span>State of the Union</span></SemanticLabel>}
        defaultOpen
        meta={`${Object.keys(agenda.state_of_the_union).length} categories`}
      >
        <div className="union-grid">
          {Object.entries(agenda.state_of_the_union).map(([key, value]) => (
            <article key={key}>
              <SemanticLabel icon={iconForAgendaCategory(key)}><strong>{key}</strong></SemanticLabel>
              <p><IconizedText maxIcons={3} text={stripLeadingSymbol(value)} /></p>
            </article>
          ))}
        </div>
      </DisclosureSection>

      <DisclosureSection title={<SemanticLabel icon={iconForSection('what_changed')}><span>What changed</span></SemanticLabel>} defaultOpen>
        <p className="notes-copy"><IconizedText maxIcons={3} text={agenda.update_notes} /></p>
      </DisclosureSection>

      <section className="priority-stack">
        <div className="section-heading">
          <span className="eyebrow">Short term</span>
          <h2>{activeShort.length} active priorities</h2>
        </div>
        {activeShort.map((item, index) => (
          <PriorityCard
            key={item.id}
            item={item}
            icon={iconForAgendaPriority(item.text)}
            rank={index + 1}
            text={stripLeadingSymbol(item.text)}
            delta={deltaFor(item, previousShort.get(item.id))}
          />
        ))}
        {closedShort.length > 0 && (
          <DisclosureSection title={<SemanticLabel icon={iconForSection('closed_items')}><span>Closed this turn</span></SemanticLabel>} meta={`${closedShort.length} items`}>
            {closedShort.map(item => (
              <PriorityCard
                key={item.id}
                item={item}
                icon={iconForAgendaPriority(item.text)}
                text={stripLeadingSymbol(item.text)}
                delta={deltaFor(item, previousShort.get(item.id))}
              />
            ))}
          </DisclosureSection>
        )}
      </section>

      <DisclosureSection
        title={<SemanticLabel icon={iconForSection('long_term_goals')}><span>Long-term goals</span></SemanticLabel>}
        defaultOpen
        meta={`${agenda.long_term.length} items`}
      >
        <div className="long-list">
          {agenda.long_term.map(item => (
            <div className={`long-row ${item.status}`} key={item.id}>
              <span>{item.status}</span>
              <SemanticIconCue icon={iconForAgendaPriority(item.text)} size="xs" />
              <p><IconizedText maxIcons={3} text={stripLeadingSymbol(item.text)} /></p>
            </div>
          ))}
        </div>
      </DisclosureSection>

      {Object.keys(agenda.cabinet_direction).length > 0 && (
        <DisclosureSection
          title={<SemanticLabel icon={iconForSection('cabinet_direction')}><span>Cabinet direction</span></SemanticLabel>}
          meta={`${Object.keys(agenda.cabinet_direction).length} ministers`}
        >
          <div className="direction-grid">
            {Object.entries(agenda.cabinet_direction).map(([minister, direction]) => (
              <article key={minister}>
                <SemanticLabel icon={iconForAgendaCategory(minister) ?? iconForField(minister)}><strong>{displayMinisterName(minister)}</strong></SemanticLabel>
                <p><IconizedText maxIcons={3} text={direction} /></p>
              </article>
            ))}
          </div>
        </DisclosureSection>
      )}
    </div>
  );
}

function AgentFlagsPanel({ flags }: { flags: AgentFlag[] }) {
  return (
    <DisclosureSection
      title={<SemanticLabel icon={iconForField('flags')}><span>Agent Flags</span></SemanticLabel>}
      defaultOpen
      meta={`${flags.length} flag${flags.length === 1 ? '' : 's'}`}
    >
      <div className="agent-flag-stack">
        {flags.map(flag => (
          <section className={`agent-flag-card ${flag.priority}`} key={flag.id}>
            <header>
              <div>
                <span className="eyebrow">{flag.domain}</span>
                <h3><IconizedText maxIcons={2} text={flag.summary} /></h3>
              </div>
              <div className="advice-badges">
                <span>{flag.priority}</span>
                {flag.detail && <span>{flag.detail}</span>}
              </div>
            </header>
            {countFlagRequests(flag) > 0 ? (
              <FlagRequestGroups flag={flag} idPrefix={`${flag.id}-request`} />
            ) : (
              <p className="flag-empty">No explicit requests attached.</p>
            )}
          </section>
        ))}
      </div>
    </DisclosureSection>
  );
}

function FlagRequestGroups({
  flag,
  idPrefix,
}: {
  flag: AgentFlag;
  idPrefix: string;
}) {
  const groups = flagRequestGroups(flag);

  return (
    <div className="typed-request-groups">
      {groups.map(group => (
        <section className="typed-request-group" key={`${idPrefix}-${group.key}`}>
          <h4><SemanticLabel icon={iconForField(group.key)}><span>{group.label}</span></SemanticLabel></h4>
          <div className="dense-table flag-request-table">
            <div className="dense-row header">
              <span>Icon</span>
              <SemanticLabel icon={iconForField('kind')}><span>Array</span></SemanticLabel>
              <SemanticLabel icon={iconForField('request')}><span>Request / reason</span></SemanticLabel>
              <SemanticLabel icon={iconForField('quantity')}><span>Details</span></SemanticLabel>
              <SemanticLabel icon={iconForField('owner')}><span>Routing</span></SemanticLabel>
            </div>
            {group.rows.map((row, index) => {
              const requestIcon = iconForField(row.iconKey);
              return (
                <div className="dense-row" key={`${idPrefix}-${group.key}-${index}`}>
                  <span className="icon-cell">
                    <GameIcon
                      fallback={requestIcon?.fallback ?? '-'}
                      label={requestIcon?.label ?? `${group.label} icon`}
                      size="xs"
                      src={iconUrlFor(requestIcon?.ref)}
                    />
                  </span>
                  <span className="flag-request-kind">
                    <strong>{group.label}</strong>
                    <code>{group.key}</code>
                  </span>
                  <span className="flag-request-copy">
                    <strong><IconizedText maxIcons={2} text={row.request} /></strong>
                    <small><IconizedText maxIcons={2} text={row.reason} /></small>
                  </span>
                  <FlagRequestDetails detail={row.detail} />
                  <FlagRequestRouting row={row} />
                </div>
              );
            })}
          </div>
        </section>
      ))}
    </div>
  );
}

type FlagRequestGroup = {
  key: 'building_requests' | 'labor_requests' | 'item_requests' | 'attention';
  label: string;
  rows: FlagRequestRow[];
};

type FlagRequestRow = {
  detail: ReactNode[];
  iconKey: string;
  owner?: string | null;
  priority?: string | null;
  reason: string;
  request: string;
  workSkill?: string | null;
};

function FlagRequestDetails({ detail }: { detail: ReactNode[] }) {
  if (detail.length === 0) {
    return <span className="flag-request-detail is-empty">-</span>;
  }

  return (
    <span className="flag-request-detail">
      {detail.map((item, index) => <small key={index}>{item}</small>)}
    </span>
  );
}

function FlagRequestRouting({ row }: { row: FlagRequestRow }) {
  const chips = [
    row.owner ? `Owner: ${row.owner}` : null,
    row.workSkill && row.workSkill !== '-' ? `Work: ${row.workSkill}` : null,
    row.priority ? `Priority: ${formatLabel(row.priority)}` : null,
  ].filter((value): value is string => Boolean(value));

  if (chips.length === 0) {
    return <span className="flag-request-routing is-empty">-</span>;
  }

  return (
    <span className="flag-request-routing">
      {chips.map(chip => <small key={chip}>{chip}</small>)}
    </span>
  );
}

function countFlagRequests(flag: AgentFlag): number {
  return (flag.building_requests?.length ?? 0) +
    (flag.labor_requests?.length ?? 0) +
    (flag.item_requests?.length ?? 0) +
    (flag.attention?.length ?? 0);
}

function flagRequestGroups(flag: AgentFlag): FlagRequestGroup[] {
  const groups: FlagRequestGroup[] = [];
  if ((flag.building_requests?.length ?? 0) > 0) {
    groups.push({
      key: 'building_requests',
      label: 'Building',
      rows: flag.building_requests!.map(buildingRequestRow),
    });
  }
  if ((flag.labor_requests?.length ?? 0) > 0) {
    groups.push({
      key: 'labor_requests',
      label: 'Labor',
      rows: flag.labor_requests!.map(laborRequestRow),
    });
  }
  if ((flag.item_requests?.length ?? 0) > 0) {
    groups.push({
      key: 'item_requests',
      label: 'Item',
      rows: flag.item_requests!.map(itemRequestRow),
    });
  }
  if ((flag.attention?.length ?? 0) > 0) {
    groups.push({
      key: 'attention',
      label: 'Attention',
      rows: flag.attention!.map(attentionRequestRow),
    });
  }
  return groups;
}

function buildingRequestRow(request: BuildingRequest): FlagRequestRow {
  return {
    detail: formatBuildingDetail(request),
    iconKey: request.target_def ? request.target_def : request.target_class,
    owner: request.requested_from,
    priority: request.priority,
    reason: request.reason,
    request: request.request,
    workSkill: null,
  };
}

function laborRequestRow(request: LaborRequest): FlagRequestRow {
  return {
    detail: detailList(formatQuantityDetail(request.quantity)),
    iconKey: 'labor_requests',
    owner: request.requested_from,
    priority: request.priority,
    reason: request.reason,
    request: request.request,
    workSkill: formatWorkSkill(request.work_type, request.skill),
  };
}

function itemRequestRow(request: ItemRequest): FlagRequestRow {
  return {
    detail: itemRequestDetails(request),
    iconKey: request.item_def ?? 'item_requests',
    owner: request.requested_from,
    priority: request.priority,
    reason: request.reason,
    request: request.request,
    workSkill: null,
  };
}

function detailList(...items: Array<ReactNode | null | undefined | false>): ReactNode[] {
  return items.filter((item): item is ReactNode => item !== null && item !== undefined && item !== false && item !== '');
}

function itemRequestDetails(request: ItemRequest): ReactNode[] {
  if (request.item_def) {
    return [
      <ResourceQuantity
        defName={request.item_def}
        key="item"
        quantity={request.quantity}
      />,
    ];
  }

  return detailList(formatQuantityDetail(request.quantity));
}

function attentionRequestRow(request: AttentionRequest): FlagRequestRow {
  return {
    detail: [],
    iconKey: 'attention',
    owner: request.requested_from,
    priority: request.priority,
    reason: request.reason,
    request: request.request,
    workSkill: null,
  };
}

function formatBuildingDetail(request: BuildingRequest): ReactNode[] {
  const details = [
    request.target_class ? `Class: ${formatLabel(request.target_class)}` : null,
    request.target_def ? `Def: ${request.target_def}` : null,
    request.room_class ? `Room: ${formatLabel(request.room_class)}` : null,
    formatCapacityNeed(request.capacity_need),
    formatAdjacency(request.adjacency),
    request.power ? `Power: ${request.power.needs_power ? 'yes' : 'no'}${request.power.approx_watts ? `, ${request.power.approx_watts}W` : ''}` : null,
    request.temperature ? `Temp: ${formatLabel(request.temperature.target_band)}${request.temperature.must_hold ? ', must hold' : ''}` : null,
    formatMaterials(request.materials_on_hand),
    request.urgency ? `Urgency: ${formatLabel(request.urgency)}` : null,
    formatDeadline(request.deadline),
    formatQuantityDetail(request.quantity),
  ].filter((value): value is ReactNode => value !== null && value !== undefined && value !== '');

  return details;
}

function formatCapacityNeed(capacity: BuildingRequest['capacity_need']): string | null {
  if (!capacity) return null;
  const amount = capacity.amount === null || capacity.amount === undefined ? null : formatInteger(capacity.amount);
  return `Capacity: ${formatLabel(capacity.measure)}${amount ? ` ${amount}` : ''}${capacity.unit ? ` ${capacity.unit}` : ''}`;
}

function formatAdjacency(adjacency: BuildingRequest['adjacency']): string | null {
  if (!adjacency || adjacency.length === 0) return null;
  return `Adjacency: ${adjacency.map(hint => `${formatLabel(hint.relation)} ${hint.target}`).join(', ')}`;
}

function formatMaterials(materials: BuildingRequest['materials_on_hand']): ReactNode | null {
  if (!materials || materials.length === 0) return null;
  return (
    <>
      <span>Materials:</span>
      <ResourceQuantityList
        items={materials.map(material => ({
          approx: material.approx_qty !== null && material.approx_qty !== undefined,
          defName: material.material,
          quantity: material.approx_qty,
        }))}
      />
    </>
  );
}

function formatDeadline(deadline: BuildingRequest['deadline']): string | null {
  if (!deadline) return null;
  return `Deadline: ${formatLabel(deadline.kind)}${deadline.value === null || deadline.value === undefined ? '' : ` ${formatLooseValue(deadline.value)}`}`;
}

function formatLooseValue(value: unknown): string {
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

function AdviceCard({
  currentGameTick,
  item,
}: {
  currentGameTick: number | null;
  item: AdviceItem;
}) {
  const [applyState, setApplyState] = useState<Record<string, ActionApplyState>>({});
  const expiry = adviceExpiryState(item, currentGameTick);

  const onApply = async (actionIndex: number) => {
    const key = actionKey(item, actionIndex);
    setApplyState(current => ({
      ...current,
      [key]: { status: 'pending', response: null, error: null },
    }));

    try {
      const response = await applyAdviceAction(item.id, actionIndex);
      setApplyState(current => ({
        ...current,
        [key]: { status: 'done', response, error: null },
      }));
    } catch (error) {
      setApplyState(current => ({
        ...current,
        [key]: { status: 'error', response: null, error: String(error) },
      }));
    }
  };

  return (
    <article className={`advice-card v2 ${item.priority} ${expiry.expired ? 'expired' : ''}`}>
      <header>
        <div>
          <span className="eyebrow">
            <SemanticLabel icon={iconForField(item.title)}><span>{item.title}</span></SemanticLabel>
          </span>
          <h3><IconizedText maxIcons={1} text={item.title} /></h3>
        </div>
        <div className="advice-badges">
          <span>{item.priority}</span>
          <span className={expiry.expired ? 'expired' : 'fresh'}>{expiry.label}</span>
        </div>
      </header>
      {expiry.message && (
        <p className={`advice-expiry-note ${expiry.expired ? 'expired' : 'fresh'}`}>
          {expiry.message}
        </p>
      )}
      <p><IconizedText maxIcons={3} text={item.body} /></p>
      <blockquote><IconizedText maxIcons={3} text={item.rationale} /></blockquote>
      {item.actions.length > 0 && (
        <DisclosureSection title={<SemanticLabel icon={iconForField('actions')}><span>Actions</span></SemanticLabel>} defaultOpen meta={`${item.actions.length} actions`}>
          <div className="action-list">
            {item.actions.map((action, index) => {
              const key = actionKey(item, index);
              const state = applyState[key] ?? { status: 'idle' as const, response: null, error: null };
              const persistedResult = action.apply_result ?? null;
              const resultStatus = state.response?.status ?? persistedResult?.status ?? state.status;
              const resultMessage = state.response?.message ?? state.error ?? persistedResult?.message ?? null;
              const resultRecordedAt = persistedResult?.recorded_at ? `Recorded ${formatDateTime(persistedResult.recorded_at)}` : null;
              const success = state.response?.status === 'applied' ||
                state.response?.status === 'already_satisfied' ||
                persistedResult?.status === 'applied' ||
                persistedResult?.status === 'already_satisfied';
              const disabled = state.status === 'pending' || success || expiry.expired;
              const actionIcon = iconForActionKind(action.kind);
              return (
                <div key={`${item.id}-action-${index}`}>
                  <GameIcon
                    fallback={actionIcon?.fallback ?? '-'}
                    label={actionIcon?.label ?? `${formatLabel(action.kind)} icon`}
                    size="xs"
                    src={iconUrlFor(actionIcon?.ref)}
                  />
                  <strong>{formatLabel(action.kind)}</strong>
                  <span><IconizedText maxIcons={2} text={action.instruction} /></span>
                  <ActionDetailLine
                    owner={action.owner}
                    quantity={action.quantity}
                    skill={action.skill}
                    workType={action.work_type}
                  />
                  {action.apply && isExecutableApply(action.apply) && (
                    <div className="action-apply">
                      <button
                        type="button"
                        disabled={disabled}
                        onClick={() => void onApply(index)}
                        title={action.apply.target_summary}
                      >
                        {expiry.expired ? 'Expired' : state.status === 'pending' ? 'Applying' : success ? 'Applied' : action.apply.label}
                      </button>
                      <small className={`action-apply-result ${resultStatus}`}>
                        {resultMessage ?? (expiry.expired ? expiry.message : action.apply.target_summary)}
                      </small>
                    </div>
                  )}
                  {!action.apply && persistedResult && (
                    <small
                      className={`action-apply-result persisted ${persistedResult.status}`}
                      title={resultRecordedAt ?? undefined}
                    >
                      {formatApplyResultText(persistedResult.status, persistedResult.message)}
                    </small>
                  )}
                </div>
              );
            })}
          </div>
        </DisclosureSection>
      )}
      {(item.suggested_actions?.length ?? 0) > 0 && (
        <DisclosureSection
          title={<SemanticLabel icon={iconForField('suggested_actions')}><span>Suggested actions</span></SemanticLabel>}
          defaultOpen
          meta={`${item.suggested_actions?.length ?? 0} actions`}
        >
          <div className="action-list">
            {item.suggested_actions?.map((action, index) => {
              const actionIcon = iconForActionKind(action.kind);
              const fallbackIconUrl = action.icon ? iconUrlFor(actionIcon?.ref) : null;
              return (
                <div key={`${item.id}-action-${index}`}>
                  <GameIcon
                    fallbackSrc={fallbackIconUrl}
                    fallback={actionIcon?.fallback ?? '-'}
                    label={actionIcon?.label ?? `${formatLabel(action.kind)} icon`}
                    size="xs"
                    src={iconUrlFor(action.icon ?? actionIcon?.ref)}
                  />
                  <strong>{action.kind}</strong>
                  <span><IconizedText maxIcons={2} text={action.instruction} /></span>
                </div>
              );
            })}
          </div>
        </DisclosureSection>
      )}
    </article>
  );
}

type ActionApplyState = {
  status: 'idle' | 'pending' | 'done' | 'error';
  response: AdviceApplyResponse | null;
  error: string | null;
};

type AdviceExpiryState = {
  expired: boolean;
  label: string;
  message: string | null;
};

const TICKS_PER_GAME_DAY = 60_000;

function adviceExpiryState(item: AdviceItem, currentGameTick: number | null): AdviceExpiryState {
  if (typeof item.stamp.expires_game_tick === 'number') {
    if (typeof currentGameTick === 'number') {
      const remaining = item.stamp.expires_game_tick - currentGameTick;
      if (remaining <= 0) {
        return {
          expired: true,
          label: 'expired',
          message: `Expired ${formatGameTickDelta(Math.abs(remaining))} ago; latest persisted advice is shown for inspection.`,
        };
      }

      return {
        expired: false,
        label: `valid ${formatGameTickDelta(remaining)}`,
        message: null,
      };
    }

    return {
      expired: false,
      label: 'game clock unknown',
      message: `Expires by game time, but the current game tick is not available.`,
    };
  }

  const expiresAtMs = Date.parse(item.stamp.expires_at);
  if (Number.isNaN(expiresAtMs)) {
    return { expired: false, label: 'freshness unknown', message: null };
  }

  if (expiresAtMs <= Date.now()) {
    return {
      expired: true,
      label: 'expired',
      message: `Expired by the older wall-clock TTL at ${formatDateTime(item.stamp.expires_at)}. Latest persisted advice is shown for inspection.`,
    };
  }

  return {
    expired: false,
    label: 'legacy TTL',
    message: null,
  };
}

function actionKey(item: AdviceItem, actionIndex: number): string {
  const action = item.actions[actionIndex];
  return `${item.id}:${item.stamp.issued_at}:${actionIndex}:${action?.kind ?? 'missing'}:${action?.instruction ?? 'missing'}`;
}

function isExecutableApply(apply: AdviceActionApply): boolean {
  return apply.kind !== 'place_blueprint_group';
}

function formatApplyResultText(status: string, message: string): string {
  if (status === 'applied') return `Applied: ${message}`;
  if (status === 'already_satisfied') return `Already satisfied: ${message}`;
  return `${formatLabel(status)}: ${message}`;
}

function PriorityCard({
  delta,
  icon,
  item,
  rank,
  text,
}: {
  delta: string | null;
  icon: ReturnType<typeof iconForAgendaPriority>;
  item: AgendaPriority;
  rank?: number;
  text: string;
}) {
  return (
    <article className={`priority-card ${item.status}`}>
      <div className="priority-card-cues">
        {rank && <span className="rank">{rank}</span>}
        <SemanticIconCue className="priority-domain-icon" icon={icon} size="xs" />
      </div>
      <p><IconizedText maxIcons={3} text={text} /></p>
      <footer>
        <span>{item.status}</span>
        {delta && <strong>{delta}</strong>}
      </footer>
    </article>
  );
}

function ActionDetailLine({
  owner,
  quantity,
  skill,
  workType,
}: {
  owner: string | null | undefined;
  quantity: number | null | undefined;
  skill: string | null | undefined;
  workType: string | null | undefined;
}) {
  const quantityDetail = formatQuantityDetail(quantity);
  const workSkillDetail = formatWorkSkillDetail(workType, skill);

  if (!quantityDetail && !owner && !workSkillDetail) return null;

  return (
    <small>
      {quantityDetail && <span>{quantityDetail}</span>}
      {owner && <span>{`Owner: ${owner}`}</span>}
      {workSkillDetail && <span>{workSkillDetail}</span>}
    </small>
  );
}

function deltaFor(current: AgendaPriority, previous: AgendaPriority | undefined): string | null {
  if (!previous) return 'new';
  if (current.status !== previous.status) return current.status;
  if (current.text !== previous.text) return 'updated';
  return null;
}

function stripLeadingSymbol(value: string): string {
  return value.trimStart().replace(/^[^\p{L}\p{N}]+/u, '').trimStart();
}

function formatTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function formatDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
}

function formatGameTickDelta(ticks: number): string {
  const days = ticks / TICKS_PER_GAME_DAY;
  if (days >= 1) return `${days.toFixed(days >= 10 ? 0 : 1)}d`;
  return `${formatInteger(Math.max(0, Math.round(ticks)))}t`;
}

function formatInteger(value: number): string {
  return Math.round(value).toLocaleString();
}

function formatLabel(value: string | null | undefined): string {
  if (!value) return '-';
  if (value === 'buildable_region') return 'Buildable region / Home area';
  return value
    .replace(/_/g, ' ')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function formatQuantityDetail(value: number | null | undefined): string | null {
  return value === null || value === undefined ? null : `Qty: ${value.toLocaleString()}`;
}

function formatWorkSkill(workType: string | null | undefined, skill: string | null | undefined): string {
  const parts = [workType, skill]
    .filter((value): value is string => Boolean(value))
    .map(formatLabel);
  return parts.length > 0 ? parts.join(' / ') : '-';
}

function formatWorkSkillDetail(workType: string | null | undefined, skill: string | null | undefined): string | null {
  const text = formatWorkSkill(workType, skill);
  return text === '-' ? null : `Work: ${text}`;
}
