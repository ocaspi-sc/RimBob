import { displayMinisterName } from '../../dashboard/scopes';
import { iconForSection } from '../../dashboard/semanticIcons';
import type { MinisterTrace } from '../../types/system';
import { IconizedText } from '../shared/IconizedText';
import { SemanticLabel } from '../shared/SemanticIcon';

export function MinisterEscalationCallout({
  canConfirmLlm = false,
  confirmDisabled = false,
  confirmPending = false,
  onConfirmLlm,
  trace,
}: {
  canConfirmLlm?: boolean;
  confirmDisabled?: boolean;
  confirmPending?: boolean;
  onConfirmLlm?: () => void;
  trace: MinisterTrace | null;
}) {
  const reason = trace?.escalationReason?.trim();
  if (!trace || !reason) return null;

  const copy = escalationCopy(trace);
  const completedAt = trace.completedAt ?? trace.startedAt;
  const ministerName = displayMinisterName(trace.minister);
  const outputCount = formatOutputCount(trace);
  const showConfirmation = trace.path === 'rules' && canConfirmLlm && onConfirmLlm !== undefined;

  return (
    <section
      aria-label={`${ministerName} escalation status`}
      className={`minister-escalation-callout ${copy.tone}`}
      role={copy.tone === 'error' ? 'alert' : 'status'}
    >
      <div className="minister-escalation-copy">
        <span className="eyebrow">{copy.kicker}</span>
        <h3>
          <SemanticLabel icon={iconForSection('llm_escalation')}>
            <span>{copy.title}</span>
          </SemanticLabel>
        </h3>
        <p>
          <strong>Reason:</strong>{' '}
          <IconizedText maxIcons={2} text={reason} />
        </p>
        <small>{copy.detail}</small>
        {showConfirmation && (
          <div className="minister-escalation-actions">
            <button
              type="button"
              className="trigger-button"
              disabled={confirmDisabled || confirmPending}
              aria-busy={confirmPending}
              onClick={onConfirmLlm}
            >
              <SemanticLabel icon={iconForSection('llm_escalation')}>
                <span>{confirmPending ? 'Running LLM...' : 'Confirm LLM'}</span>
              </SemanticLabel>
            </button>
          </div>
        )}
      </div>
      <dl className="minister-escalation-meta">
        <div>
          <dt>Trigger</dt>
          <dd>{formatToken(trace.trigger)}</dd>
        </div>
        <div>
          <dt>Path</dt>
          <dd>{formatToken(trace.path)}</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>{formatToken(trace.status)}</dd>
        </div>
        <div>
          <dt>Trace time</dt>
          <dd>{formatTraceTime(completedAt)}</dd>
        </div>
        {outputCount && (
          <div>
            <dt>Output</dt>
            <dd>{outputCount}</dd>
          </div>
        )}
      </dl>
    </section>
  );
}

function escalationCopy(trace: MinisterTrace): { detail: string; kicker: string; title: string; tone: 'error' | 'ok' | 'warn' } {
  if (trace.path === 'rules') {
    return {
      detail: 'The deterministic rules path requested provider judgment; the LLM path is paused until the player confirms it from the dashboard.',
      kicker: 'Rules requested escalation',
      title: 'Needs LLM judgment',
      tone: 'warn',
    };
  }

  if (trace.path === 'llm_failed') {
    return {
      detail: trace.errorMessage ? `The provider path failed after escalation: ${trace.errorMessage}` : 'The provider path failed after escalation.',
      kicker: 'Escalation failed',
      title: 'LLM path did not complete',
      tone: 'error',
    };
  }

  if (trace.path === 'llm') {
    return {
      detail: 'The latest minister run used the provider path and accepted output.',
      kicker: 'Escalation used',
      title: 'LLM judgment shaped this output',
      tone: 'ok',
    };
  }

  return {
    detail: 'The latest trace recorded an escalation reason for this minister run.',
    kicker: 'Escalation recorded',
    title: 'Minister escalation signal',
    tone: 'warn',
  };
}

function formatOutputCount(trace: MinisterTrace): string | null {
  const parts = [
    trace.adviceCount !== null ? `${trace.adviceCount} advice` : null,
    trace.flagCount !== null ? `${trace.flagCount} flags` : null,
  ].filter((part): part is string => part !== null);

  return parts.length > 0 ? parts.join(' / ') : null;
}

function formatToken(value: string): string {
  return value.replace(/_/g, ' ');
}

function formatTraceTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  return date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}
