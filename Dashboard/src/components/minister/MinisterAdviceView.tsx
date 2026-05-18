import { useState } from 'react';
import type { MayorAgenda, AgendaPriority } from '../../types/agenda';
import type { AdviceApplyResponse, AdviceChainModel, AdviceItem } from '../../types/advice';
import type { ScopeConfig } from '../../dashboard/scopes';
import { applyAdviceAction } from '../../api/advice';
import { iconUrlFor } from '../../api/icons';
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
import { ChainTable } from '../shared/ChainTable';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { IconizedText } from '../shared/IconizedText';
import { SemanticIconCue, SemanticLabel } from '../shared/SemanticIcon';

export function MinisterAdviceView({
  advice,
  agenda,
  chain,
  previousAgenda,
  scope,
  stateSummary,
}: {
  advice: AdviceItem[];
  agenda: MayorAgenda | null;
  chain: AdviceChainModel | null;
  previousAgenda: MayorAgenda | null;
  scope: ScopeConfig;
  stateSummary: string | null;
}) {
  if (scope.key === 'mayor') {
    return <MayorAdvice agenda={agenda} previousAgenda={previousAgenda} />;
  }

  const ministerAdvice = advice.filter(item => item.minister.toLowerCase() === scope.label.toLowerCase());

  if (scope.status !== 'live') {
    return <EmptyState code="ADVICE NOT WIRED">{scope.label} is planned and not emitting advice yet.</EmptyState>;
  }

  if (ministerAdvice.length === 0 && !stateSummary) {
    return <EmptyState code="NO ACTIVE ADVICE">{scope.label} has not emitted active advice in this session.</EmptyState>;
  }

  const currentStateLines = stateSummary ? splitStateSummary(stateSummary) : [];
  const hasChain = Boolean(chain?.paths.length);

  return (
    <div className="minister-view advice-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2><SemanticLabel icon={iconForView('advice')}><span>Advice</span></SemanticLabel></h2>
        <p>Active feeder minister advice from the SSE feed.</p>
      </header>
      {stateSummary && (
        <section className="advice-state-summary">
          <span className="eyebrow">Current State</span>
          {currentStateLines.length > 0 ? (
            <table className="state-summary-table" aria-label={`${scope.label} current state summary`}>
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
      {hasChain && chain && (
        <section className="advice-chain">
          <div className="advice-chain-heading">
            <span className="eyebrow">Food Routes - 4 Treatments</span>
          </div>
          <ChainTable model={chain} />
        </section>
      )}
      {ministerAdvice.length === 0 && (
        <EmptyState code="NO ACTIVE ADVICE">{scope.label} has no active advice items for this state.</EmptyState>
      )}
      <div className="advice-stack">
        {ministerAdvice.map(item => <AdviceCard key={item.id} item={item} />)}
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
  crops: 'crops',
  'confidence gaps': 'data_coverage',
  'kitchen/storage': 'kitchen',
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
  previousAgenda,
}: {
  agenda: MayorAgenda | null;
  previousAgenda: MayorAgenda | null;
}) {
  if (!agenda) {
    return <EmptyState code="NO AGENDA">Waiting for the Mayor's first agenda update.</EmptyState>;
  }

  const previousShort = new Map((previousAgenda?.short_term ?? []).map(item => [item.id, item]));
  const activeShort = agenda.short_term.filter(item => item.status === 'active');
  const closedShort = agenda.short_term.filter(item => item.status !== 'active');

  return (
    <div className="minister-view advice-view mayor-advice">
      <header className="agenda-hero">
        <div>
          <span className="eyebrow">Mayor Advice</span>
          <h2><IconizedText maxIcons={3} text={agenda.posture.summary} /></h2>
        </div>
        <div className="agenda-stamps">
          <span>v{agenda.version}</span>
          <span>{agenda.updated_in_game_tick}</span>
          <span>{formatTime(agenda.generated_at)}</span>
        </div>
      </header>

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
                <SemanticLabel icon={iconForAgendaCategory(minister) ?? iconForField(minister)}><strong>{minister}</strong></SemanticLabel>
                <p><IconizedText maxIcons={3} text={direction} /></p>
              </article>
            ))}
          </div>
        </DisclosureSection>
      )}
    </div>
  );
}

function AdviceCard({ item }: { item: AdviceItem }) {
  const [applyState, setApplyState] = useState<Record<string, ActionApplyState>>({});

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
    <article className={`advice-card v2 ${item.priority}`}>
      <header>
        <div>
          <span className="eyebrow">
            <SemanticLabel icon={iconForField(item.advice_type)}><span>{item.advice_type}</span></SemanticLabel>
          </span>
          <h3><IconizedText maxIcons={1} text={item.title} /></h3>
        </div>
        <div className="advice-badges">
          <span>{item.priority}</span>
        </div>
      </header>
      <p><IconizedText maxIcons={3} text={item.body} /></p>
      <blockquote><IconizedText maxIcons={3} text={item.rationale} /></blockquote>
      {item.actions.length > 0 && (
        <DisclosureSection title={<SemanticLabel icon={iconForField('actions')}><span>Actions</span></SemanticLabel>} defaultOpen meta={`${item.actions.length} actions`}>
          <div className="action-list">
            {item.actions.map((action, index) => {
              const key = actionKey(item, index);
              const state = applyState[key] ?? { status: 'idle' as const, response: null, error: null };
              const success = state.response?.status === 'applied' || state.response?.status === 'already_satisfied';
              const disabled = state.status === 'pending' || success;
              const actionIcon = iconForActionKind(action.kind);
              return (
                <div key={`${item.id}-action-${index}`}>
                  <GameIcon
                    fallback={actionIcon?.fallback ?? '-'}
                    label={actionIcon?.label ?? `${formatLabel(action.kind)} icon`}
                    size="xs"
                    src={iconUrlFor(action.icon ?? actionIcon?.ref)}
                  />
                  <strong>{formatLabel(action.kind)}</strong>
                  <span><IconizedText maxIcons={2} text={action.instruction} /></span>
                  <ActionDetailLine
                    owner={action.owner}
                    quantity={action.quantity}
                    reason={action.reason}
                    skill={action.skill}
                    workType={action.work_type}
                  />
                  {action.apply && (
                    <div className="action-apply">
                      <button
                        type="button"
                        disabled={disabled}
                        onClick={() => void onApply(index)}
                        title={action.apply.target_summary}
                      >
                        {state.status === 'pending' ? 'Applying' : success ? 'Applied' : action.apply.label}
                      </button>
                      <small className={`action-apply-result ${state.response?.status ?? state.status}`}>
                        {state.response?.message ?? state.error ?? action.apply.target_summary}
                      </small>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </DisclosureSection>
      )}
      {(item.resource_requests?.length ?? 0) > 0 && (
        <DisclosureSection
          title={<SemanticLabel icon={iconForField('resource_requests')}><span>Resource requests</span></SemanticLabel>}
          defaultOpen
          meta={`${item.resource_requests?.length ?? 0} requests`}
        >
          <div className="dense-table resource-table">
            <div className="dense-row header">
              <span>Icon</span>
              <SemanticLabel icon={iconForField('kind')}><span>Kind</span></SemanticLabel>
              <SemanticLabel icon={iconForField('request')}><span>Request</span></SemanticLabel>
              <SemanticLabel icon={iconForField('reason')}><span>Reason</span></SemanticLabel>
              <SemanticLabel icon={iconForField('quantity')}><span>Qty</span></SemanticLabel>
              <SemanticLabel icon={iconForField('owner')}><span>Owner</span></SemanticLabel>
              <SemanticLabel icon={iconForField('work_type')}><span>Work / Skill</span></SemanticLabel>
              <SemanticLabel icon={iconForField('priority')}><span>Priority</span></SemanticLabel>
            </div>
            {item.resource_requests?.map((request, index) => {
              const requestIcon = iconForActionKind(request.kind);
              return (
                <div className="dense-row" key={`${item.id}-request-${index}`}>
                  <span className="icon-cell">
                    <GameIcon
                      fallback={requestIcon?.fallback ?? '-'}
                      label={requestIcon?.label ?? `${formatLabel(request.kind)} icon`}
                      size="xs"
                      src={iconUrlFor(request.icon ?? requestIcon?.ref)}
                    />
                  </span>
                  <span>{formatLabel(request.kind)}</span>
                  <span><IconizedText maxIcons={2} text={request.request} /></span>
                  <span><IconizedText maxIcons={2} text={request.reason} /></span>
                  <span>{formatQuantity(request.quantity)}</span>
                  <span>{request.requested_from ?? '-'}</span>
                  <span>{formatWorkSkill(request.work_type, request.skill)}</span>
                  <span>{formatLabel(request.priority)}</span>
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
              return (
                <div key={`${item.id}-action-${index}`}>
                  <GameIcon
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

function actionKey(item: AdviceItem, actionIndex: number): string {
  const action = item.actions[actionIndex];
  const applyContext = action?.apply
    ? `${action.apply.kind}:${action.apply.target_summary}:${action.apply.target_ids?.join(',') ?? ''}:${action.apply.thing_ids?.join(',') ?? ''}`
    : 'text';
  return `${item.id}:${item.issued_at}:${actionIndex}:${applyContext}`;
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
  reason,
  skill,
  workType,
}: {
  owner: string | null | undefined;
  quantity: number | null | undefined;
  reason: string | null | undefined;
  skill: string | null | undefined;
  workType: string | null | undefined;
}) {
  const quantityDetail = formatQuantityDetail(quantity);
  const workSkillDetail = formatWorkSkillDetail(workType, skill);

  if (!quantityDetail && !owner && !workSkillDetail && !reason) return null;

  return (
    <small>
      {quantityDetail && <span>{quantityDetail}</span>}
      {owner && <span>{`Owner: ${owner}`}</span>}
      {workSkillDetail && <span>{workSkillDetail}</span>}
      {reason && <span><IconizedText maxIcons={2} text={reason} /></span>}
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

function formatLabel(value: string | null | undefined): string {
  if (!value) return '-';
  return value
    .replace(/_/g, ' ')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function formatQuantity(value: number | null | undefined): string {
  return value === null || value === undefined ? '-' : value.toLocaleString();
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
