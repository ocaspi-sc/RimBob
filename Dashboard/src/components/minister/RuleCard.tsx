import { iconUrlFor } from '../../api/icons';
import {
  iconForField,
  iconForFieldValue,
  iconForRuleOutcome,
  type SemanticIconSpec,
} from '../../dashboard/semanticIcons';
import type { RuleEmission, RuleEvaluationTrace } from '../../types/system';
import { GameIcon } from '../shared/GameIcon';
import { IconizedText } from '../shared/IconizedText';
import { SemanticLabel } from '../shared/SemanticIcon';

export function RuleCard({ rule, selected }: { rule: RuleEvaluationTrace; selected: boolean }) {
  const outcome = rule.outcome?.trim() || 'unknown';
  const evidence = (rule.reason ?? rule.conditions ?? '').trim();
  const priority = firstNonEmpty(rule.emissions.map(emission => emission.priority));

  return (
    <article className={`rule-card ${outcome}${selected ? ' is-selected' : ''}`}>
      <div className="rule-card-identity">
        <SemanticLabel className="rule-card-outcome" icon={iconForRuleOutcome(outcome) ?? iconForField('outcome')}>
          <span>{formatRuleOutcome(outcome)}</span>
        </SemanticLabel>
        <SemanticLabel className="rule-card-id" icon={iconForField('rule')}>
          <code>{rule.rule}</code>
        </SemanticLabel>
        {priority && (
          <small className="rule-card-chip">
            <SemanticLabel icon={iconForField('priority')}>
              <span>{formatRuleOutcome(priority)}</span>
            </SemanticLabel>
          </small>
        )}
      </div>

      <div className="rule-card-evidence">
        <small className="rule-card-column-label">
          <SemanticLabel icon={iconForField('reason')}>
            <span>Evidence</span>
          </SemanticLabel>
        </small>
        {evidence ? (
          <span><IconizedText maxIcons={3} text={evidence} /></span>
        ) : (
          <span className="rule-card-muted">No evidence reported</span>
        )}
      </div>

      <div className="rule-card-emits">
        <small className="rule-card-column-label">
          <SemanticLabel icon={iconForField('outputs')}>
            <span>Emits</span>
          </SemanticLabel>
        </small>
        {rule.emissions.length > 0 ? (
          rule.emissions.map((emission, index) => (
            <EmissionRow emission={emission} key={`${rule.rule}-${emission.kind}-${index}`} />
          ))
        ) : (
          <span className="rule-card-muted">-</span>
        )}
      </div>
    </article>
  );
}

function EmissionRow({ emission }: { emission: RuleEmission }) {
  const kind = emission.kind.trim().toLowerCase() || 'unknown';
  const icon = iconForEmission(emission, kind);
  const iconLabel = emission.label.trim() || formatRuleOutcome(kind);
  const chips = emissionChips(emission, kind);

  return (
    <div className={`rule-card-emit${kind === 'escalate' ? ' escalate' : ''}`}>
      <GameIcon
        fallback={icon?.fallback ?? fallbackLetters(iconLabel)}
        label={icon?.label ?? `${iconLabel} icon`}
        size="xs"
        src={iconUrlFor(icon?.ref)}
      />
      <strong><IconizedText maxIcons={2} text={emission.label} /></strong>
      {chips.map(chip => (
        <small className="rule-card-chip" key={chip.key}>
          <SemanticLabel icon={chip.icon}>
            <span>{chip.label}</span>
          </SemanticLabel>
        </small>
      ))}
    </div>
  );
}

function iconForEmission(emission: RuleEmission, kind: string): SemanticIconSpec | undefined {
  if (kind === 'advise') return iconForField(emission.label) ?? iconForField('advice');
  if (kind === 'request_build') return iconForFieldValue('target_def', emission.targetDef ?? '') ?? iconForField('building_requests');
  if (kind === 'request_labor') return iconForField('labor_requests');
  if (kind === 'request_item') return iconForFieldValue('item_def', emission.targetDef ?? '') ?? iconForField('item_requests');
  if (kind === 'request_attention') return iconForField('attention');
  if (kind === 'escalate') return iconForField('llm_escalation');
  return iconForField(kind) ?? iconForField('outputs');
}

function emissionChips(
  emission: RuleEmission,
  kind: string,
): Array<{ icon: SemanticIconSpec | undefined; key: string; label: string }> {
  const chips: Array<{ icon: SemanticIconSpec | undefined; key: string; label: string }> = [];
  const to = cleanText(emission.to);
  const priority = cleanText(emission.priority);
  const workType = kind === 'request_labor' ? cleanText(emission.workType) : null;

  if (to) chips.push({ icon: iconForField('owner'), key: `to-${to}`, label: `to ${to}` });
  if (priority) chips.push({ icon: iconForField('priority'), key: `priority-${priority}`, label: formatRuleOutcome(priority) });
  if (workType) chips.push({ icon: iconForField('work_type'), key: `work-${workType}`, label: formatRuleOutcome(workType) });

  return chips;
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

function fallbackLetters(value: string): string {
  const compact = value.replace(/[^a-zA-Z0-9]+/g, '').toUpperCase();
  return compact.slice(0, 2) || '??';
}

export function formatRuleOutcome(outcome: string): string {
  return outcome
    .replace(/_/g, ' ')
    .replace(/\b\w/g, letter => letter.toUpperCase());
}
