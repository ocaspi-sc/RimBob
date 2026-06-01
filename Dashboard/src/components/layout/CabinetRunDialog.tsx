import type { CabinetRunLogSnapshot, CabinetRunStepSnapshot } from '../../types/system';

export function CabinetRunDialog({
  onClose,
  open,
  run,
}: {
  onClose: () => void;
  open: boolean;
  run: CabinetRunLogSnapshot | null;
}) {
  if (!open || !run) return null;

  const statusLabel = formatLabel(run.status);
  const completed = run.status !== 'running';

  return (
    <div className="cabinet-run-overlay">
      <section
        aria-label="Cabinet run log"
        aria-modal="false"
        className={`cabinet-run-dialog ${run.status}`}
        role="dialog"
      >
        <header className="cabinet-run-header">
          <div>
            <span className="eyebrow">Manual cabinet run</span>
            <h2>{statusLabel}</h2>
          </div>
          <div className="cabinet-run-header-actions">
            <span className={`cabinet-run-status ${run.status}`}>{statusLabel}</span>
            <button
              aria-label="Close cabinet run dialog"
              className="cabinet-run-close"
              onClick={onClose}
              title={completed ? 'Close run log.' : 'Close run log without canceling the backend run.'}
              type="button"
            >
              x
            </button>
          </div>
        </header>

        <div className="cabinet-run-meta">
          <span title={run.run_id}>{shortRunId(run.run_id)}</span>
          <span>{formatTime(run.started_at)}</span>
          <span>{formatDuration(run.duration_ms)}</span>
          {run.state_source && <span>{run.state_source}</span>}
          {run.used_restored_snapshot && <span>restored snapshot</span>}
        </div>

        <ol className="cabinet-run-steps">
          {run.steps.map(step => (
            <CabinetRunStepRow key={step.key} step={step} />
          ))}
        </ol>
      </section>
    </div>
  );
}

function CabinetRunStepRow({ step }: { step: CabinetRunStepSnapshot }) {
  const detail = step.error_message ?? step.detail ?? step.trace_note;
  const traceSummary = compactTraceSummary(step);

  return (
    <li className={`cabinet-run-step ${step.status}`}>
      <span className={`cabinet-run-step-dot ${step.status}`} aria-hidden="true" />
      <div className="cabinet-run-step-main">
        <div className="cabinet-run-step-title">
          <strong>{step.label}</strong>
          <span>{formatTime(step.started_at)}</span>
          <span>{formatDuration(step.duration_ms)}</span>
        </div>
        {detail && <p>{detail}</p>}
        {traceSummary && <small>{traceSummary}</small>}
      </div>
    </li>
  );
}

function compactTraceSummary(step: CabinetRunStepSnapshot): string | null {
  const parts: string[] = [];
  if (step.trace_path) parts.push(step.trace_path);
  if (step.rule_fired) parts.push(`rule ${step.rule_fired}`);
  if (!step.rule_fired && step.escalation_reason) parts.push(step.escalation_reason);
  if (step.advice_count !== null) parts.push(`${step.advice_count} advice`);
  if (step.flag_count !== null) parts.push(`${step.flag_count} flags`);
  if (step.error_type && !step.error_message) parts.push(step.error_type);
  return parts.length === 0 ? null : parts.join(' | ');
}

function formatDuration(durationMs: number | null): string {
  if (durationMs === null) return 'running';
  if (durationMs < 1000) return `${durationMs} ms`;
  return `${(durationMs / 1000).toFixed(1)} s`;
}

function formatLabel(value: string): string {
  return value
    .replace(/_/g, ' ')
    .replace(/\b\w/g, letter => letter.toUpperCase());
}

function formatTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleTimeString();
}

function shortRunId(runId: string): string {
  if (runId.length <= 12) return runId;
  return `${runId.slice(0, 8)}...`;
}
