import { useState } from 'react';
import { applyAdviceAction } from '../../api/advice';
import { iconUrlFor } from '../../api/icons';
import {
  iconForField,
  iconForFieldValue,
  type SemanticIconSpec,
} from '../../dashboard/semanticIcons';
import type { AdviceAction, AdviceApplyResponse, AdviceItem } from '../../types/advice';
import type { RuleEmission, RuleEvaluationTrace } from '../../types/system';
import { GameIcon } from '../shared/GameIcon';
import { IconizedText } from '../shared/IconizedText';
import { SemanticLabel } from '../shared/SemanticIcon';

export function RuleCard({
  advice,
  onOpenAdvice,
  rule,
  selected,
}: {
  advice: AdviceItem | undefined;
  onOpenAdvice: (adviceId: string) => void;
  rule: RuleEvaluationTrace;
  selected: boolean;
}) {
  const [applyState, setApplyState] = useState<Record<string, RuleApplyState>>({});
  const outcome = rule.outcome?.trim() || 'unknown';
  const evidence = (rule.reason ?? rule.conditions ?? '').trim();
  const priority = firstNonEmpty(rule.emissions.map(emission => emission.priority));
  const adviceEmissions = rule.emissions.filter(isAdviceEmission);
  const tableEmissions = rule.emissions.filter(emission => !isAdviceEmission(emission));
  const onApplyAction = async (target: RuleApplyTarget) => {
    const key = applyStateKey(target);
    setApplyState(current => ({
      ...current,
      [key]: { status: 'pending', response: null, error: null },
    }));

    try {
      const response = await applyAdviceAction(target.advice.id, target.actionIndex);
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
    <article
      aria-label={`${rule.rule} rule, ${formatRuleOutcome(outcome)}`}
      className={`rule-card ${outcome}${selected ? ' is-selected' : ''}`}
    >
      <div className="rule-card-title-row">
        <span className="rule-card-title">
          <code>{rule.rule}</code>
        </span>
        {adviceEmissions.map((emission, index) => (
          <AdviceEmissionInline
            advice={advice}
            emission={emission}
            key={`${rule.rule}-${emission.kind}-${index}`}
            onOpenAdvice={onOpenAdvice}
          />
        ))}
        {rule.emissions.length === 0 && <NoEmissionsMarker />}
        {priority && (
          <small className="rule-card-chip">
            <SemanticLabel icon={iconForField('priority')}>
              <span>{formatRuleOutcome(priority)}</span>
            </SemanticLabel>
          </small>
        )}
      </div>

      <div className="rule-card-evidence-row">
        <small className="rule-card-row-label">
          <span>Evidence</span>
        </small>
        {evidence ? (
          <span className="rule-card-evidence-text"><IconizedText maxIcons={3} text={evidence} /></span>
        ) : (
          <span className="rule-card-muted">No evidence reported</span>
        )}
      </div>

      {tableEmissions.length > 0 ? (
        <table className="rule-emits-table">
          <colgroup>
            <col className="rule-emits-col-priority-bar" />
            <col className="rule-emits-col-type" />
            <col className="rule-emits-col-apply" />
            <col className="rule-emits-col-to" />
            <col className="rule-emits-col-target" />
            <col className="rule-emits-col-work" />
            <col className="rule-emits-col-details" />
          </colgroup>
          <thead>
            <tr>
              <th aria-label="Priority signal" />
              <th>Type</th>
              <th>Apply</th>
              <th>ToMinister</th>
              <th>Target</th>
              <th>Work</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
            {tableEmissions.map((emission, index) => (
              <EmissionRow
                advice={advice}
                applyState={applyState}
                emission={emission}
                key={`${rule.rule}-${emission.kind}-${index}`}
                onApplyAction={onApplyAction}
              />
            ))}
          </tbody>
        </table>
      ) : null}
    </article>
  );
}

function NoEmissionsMarker() {
  return (
    <small className="rule-card-chip rule-card-no-emissions-marker">
      <SemanticLabel icon={iconForField('outputs')}>
        <span>No emits</span>
      </SemanticLabel>
    </small>
  );
}

function AdviceEmissionInline({
  advice,
  emission,
  onOpenAdvice,
}: {
  advice: AdviceItem | undefined;
  emission: RuleEmission;
  onOpenAdvice: (adviceId: string) => void;
}) {
  const icon = iconForEmission(emission, 'advise');
  const iconLabel = emission.label.trim() || 'Advice';

  return (
    <span className="rule-card-advice-inline">
      <small className="rule-card-advice-label">
        <SemanticLabel icon={iconForField('advice')}>
          <span>Advice</span>
        </SemanticLabel>
      </small>
      <span className="rule-card-advice-text">
        <GameIcon
          fallback={icon?.fallback ?? fallbackLetters(iconLabel)}
          label={icon?.label ?? `${iconLabel} icon`}
          size="xs"
          src={iconUrlFor(icon?.ref)}
        />
        <strong><IconizedText maxIcons={2} text={emission.label} /></strong>
      </span>
      {advice && <AdviceLinkButton advice={advice} onOpenAdvice={onOpenAdvice} />}
    </span>
  );
}

function EmissionRow({
  advice,
  applyState,
  emission,
  onApplyAction,
}: {
  advice: AdviceItem | undefined;
  applyState: Record<string, RuleApplyState>;
  emission: RuleEmission;
  onApplyAction: (target: RuleApplyTarget) => void;
}) {
  const kind = emission.kind.trim().toLowerCase() || 'unknown';
  const icon = iconForEmission(emission, kind);
  const iconLabel = emission.label.trim() || formatRuleOutcome(kind);
  const target = cleanText(emission.targetDef);
  const targetIcon = target ? iconForFieldValue('target_def', target) ?? iconForFieldValue('item_def', target) : undefined;
  const priorityTone = priorityClass(emission.priority);
  const applyTarget = resolveApplyTarget(advice, emission);

  return (
    <tr className={`rule-emits-row${kind === 'escalate' ? ' escalate' : ''}`}>
      <td>
        <span
          aria-label={`${formatRuleOutcome(emission.priority ?? 'none')} priority`}
          className={`rule-emits-priority-bar ${priorityTone}`}
          title={`${formatRuleOutcome(emission.priority ?? 'none')} priority`}
        />
      </td>
      <td>
        <SemanticLabel icon={icon}>
          <span>{formatRuleOutcome(kind)}</span>
        </SemanticLabel>
      </td>
      <td>
        {applyTarget ? (
          <ApplyButton
            onApplyAction={onApplyAction}
            state={applyState[applyStateKey(applyTarget)]}
            target={applyTarget}
          />
        ) : (
          <MutedDash />
        )}
      </td>
      <td>{emission.to ? formatRuleOutcome(emission.to) : <MutedDash />}</td>
      <td>
        {target ? (
          <SemanticLabel icon={targetIcon}>
            <span>{target}</span>
          </SemanticLabel>
        ) : (
          <MutedDash />
        )}
      </td>
      <td>{emission.workType ? formatRuleOutcome(emission.workType) : <MutedDash />}</td>
      <td>
        <span className="rule-emits-detail">
          <GameIcon
            fallback={icon?.fallback ?? fallbackLetters(iconLabel)}
            label={icon?.label ?? `${iconLabel} icon`}
            size="xs"
            src={iconUrlFor(icon?.ref)}
          />
          <strong><IconizedText maxIcons={2} text={emission.label} /></strong>
        </span>
      </td>
    </tr>
  );
}

function MutedDash() {
  return <span className="rule-card-muted">-</span>;
}

function AdviceLinkButton({
  advice,
  onOpenAdvice,
}: {
  advice: AdviceItem;
  onOpenAdvice: (adviceId: string) => void;
}) {
  return (
    <button
      aria-label={`Open advice: ${advice.title}`}
      className="rule-card-advice-link"
      onClick={() => onOpenAdvice(advice.id)}
      title={`Open advice: ${advice.title}`}
      type="button"
    >
      🔗
    </button>
  );
}

function ApplyButton({
  onApplyAction,
  state,
  target,
}: {
  onApplyAction: (target: RuleApplyTarget) => void;
  state: RuleApplyState | undefined;
  target: RuleApplyTarget;
}) {
  const success = state?.response?.status === 'applied' ||
    state?.response?.status === 'already_satisfied' ||
    target.action.apply_result?.status === 'applied' ||
    target.action.apply_result?.status === 'already_satisfied';
  const disabled = state?.status === 'pending' || success;
  const label = success ? 'Applied' : state?.status === 'pending' ? 'Applying' : state?.status === 'error' ? 'Retry' : 'Apply';
  const title = state?.error ??
    state?.response?.message ??
    target.action.apply_result?.message ??
    target.action.apply?.target_summary ??
    `Apply ${formatRuleOutcome(target.action.kind)}`;

  return (
    <button
      className={`rule-emits-apply-button rule-emits-table-apply-button ${success ? 'applied' : ''} ${state?.status === 'error' ? 'error' : ''}`}
      disabled={disabled}
      onClick={() => void onApplyAction(target)}
      title={title}
      type="button"
    >
      {label}
    </button>
  );
}

function iconForEmission(emission: RuleEmission, kind: string): SemanticIconSpec | undefined {
  if (kind === 'advise') return iconForField(emission.label) ?? iconForField('advice');
  if (kind === 'request_build') return iconForFieldValue('target_def', emission.targetDef ?? '') ?? iconForField('building_requests');
  if (kind === 'request_labor') return iconForField('labor_requests');
  if (kind === 'request_item') return iconForFieldValue('item_def', emission.targetDef ?? '') ?? iconForField('item_requests');
  if (kind === 'request_zone') return iconForFieldValue('plant_def', emission.targetDef ?? '') ?? iconForField('zone_requests');
  if (kind === 'request_attention') return iconForField('attention');
  if (kind === 'escalate') return iconForField('llm_escalation');
  return iconForField(kind) ?? iconForField('outputs');
}

type RuleApplyState = {
  status: 'pending' | 'done' | 'error';
  response: AdviceApplyResponse | null;
  error: string | null;
};

type RuleApplyTarget = {
  action: AdviceAction;
  actionIndex: number;
  advice: AdviceItem;
};

function resolveApplyTarget(advice: AdviceItem | undefined, emission: RuleEmission): RuleApplyTarget | null {
  if (!advice) return null;

  const executableTargets = advice.actions
    .map((action, actionIndex) => ({ action, actionIndex, advice }))
    .filter(target => target.action.apply && isExecutableRulesApply(target.action));

  return executableTargets.find(target => actionMatchesEmission(target.action, emission)) ?? null;
}

function isExecutableRulesApply(action: AdviceAction): boolean {
  return action.apply !== null &&
    action.apply !== undefined &&
    action.apply.kind !== 'place_blueprint_group';
}

function actionMatchesEmission(action: AdviceAction, emission: RuleEmission): boolean {
  const emissionKind = normalizeActionToken(emission.kind);
  const actionKind = normalizeActionToken(action.kind);

  if (emissionKind === 'request_labor') {
    const emissionWorkType = normalizeActionToken(emission.workType);
    const actionWorkType = normalizeActionToken(action.work_type);
    if (emissionWorkType && emissionWorkType === actionWorkType) return true;

    return normalizeActionToken(emission.to) === 'labor' &&
      (actionKind === 'mark_harvest' || actionKind === 'mark_hunt');
  }

  if (emissionKind === 'request_item') {
    return action.apply?.kind === 'unforbid_things' || actionKind === 'unforbid';
  }

  if (emissionKind === 'request_zone') {
    return actionKind === 'designate_zone_req';
  }

  if (emissionKind === 'request_attention') {
    return false;
  }

  if (emissionKind === 'request_build') {
    return actionKind === 'place_blueprint' && action.apply !== null && action.apply !== undefined;
  }

  return false;
}

function applyStateKey(target: RuleApplyTarget): string {
  return `${target.advice.id}:${target.actionIndex}`;
}

function firstNonEmpty(values: Array<string | null | undefined>): string | null {
  for (const value of values) {
    const text = cleanText(value);
    if (text) return text;
  }

  return null;
}

function cleanText(value: string | null | undefined): string | null {
  const text = value?.trim();
  return text ? text : null;
}

function isAdviceEmission(emission: RuleEmission): boolean {
  return emission.kind.trim().toLowerCase() === 'advise';
}

function fallbackLetters(value: string): string {
  const compact = value.replace(/[^a-zA-Z0-9]+/g, '').toUpperCase();
  return compact.slice(0, 2) || '??';
}

function priorityClass(priority: string | null | undefined): string {
  const normalized = cleanText(priority)?.toLowerCase() ?? 'none';
  return normalized.replace(/[^a-z0-9_-]/g, '') || 'none';
}

function normalizeActionToken(value: string | null | undefined): string {
  return value?.trim().toLocaleLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '') ?? '';
}

export function formatRuleOutcome(outcome: string): string {
  return outcome
    .replace(/_/g, ' ')
    .replace(/\b\w/g, letter => letter.toUpperCase());
}
