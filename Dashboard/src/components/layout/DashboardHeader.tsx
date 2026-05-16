import type { RimBobStatus } from '../../types/status';
import type { StreamDiagnostics } from '../../types/system';
import { StatusPill } from '../shared/StatusPill';

export function DashboardHeader({
  onTriggerCabinet,
  status,
  stream,
  triggerDisabled,
  triggerError,
  triggerPending,
}: {
  onTriggerCabinet: () => void;
  status: RimBobStatus | null;
  stream: StreamDiagnostics;
  triggerDisabled: boolean;
  triggerError: string | null;
  triggerPending: boolean;
}) {
  const llmStatus = status?.llm_status ?? (status?.llm_configured ? 'ready' : 'missing_key');
  const llmTone = llmToneFor(llmStatus);
  const llmLabel = llmLabelFor(llmStatus);
  const mayorTone = !status
    ? 'idle'
    : status.mayor_last_error
      ? 'error'
      : status.mayor_running
        ? 'info'
        : 'ok';

  return (
    <header className="dashboard-header panel-shell">
      <div className="brand-block">
        <span className="eyebrow">RimWorld Advisory Cabinet</span>
        <h1>RimBob Dashboard v2</h1>
      </div>
      <div className="header-controls">
        <div className="header-status">
          <StatusPill tone={status ? 'ok' : 'idle'}>Host {status?.server ?? 'checking'}</StatusPill>
          <StatusPill tone={status?.rimapi_reachable ? 'ok' : 'warn'}>RIMAPI {status?.rimapi_reachable ? 'live' : 'waiting'}</StatusPill>
          <StatusPill tone={status ? llmTone : 'idle'}>LLM {status ? llmLabel : 'checking'}</StatusPill>
          <StatusPill tone={stream.state === 'open' ? 'ok' : stream.state === 'error' ? 'warn' : 'idle'}>SSE {stream.state}</StatusPill>
          <StatusPill tone={mayorTone}>Mayor {status?.mayor_running ? 'running' : status?.mayor_last_error ? 'error' : 'idle'}</StatusPill>
        </div>
        <button
          type="button"
          className="trigger-button global"
          disabled={triggerDisabled}
          aria-busy={triggerPending}
          onClick={onTriggerCabinet}
          title="Trigger all live ministers manually"
        >
          {triggerPending ? 'Running Cabinet...' : 'Run Cabinet Now'}
        </button>
        {triggerError && <span className="trigger-error" role="status">{triggerError}</span>}
      </div>
    </header>
  );
}

function llmToneFor(status: string): 'ok' | 'warn' | 'error' | 'idle' {
  if (status === 'missing_key' || status === 'request_failed' || status === 'parse_failed') return 'error';
  if (status === 'ready' || status === 'not_seen_yet') return 'warn';
  if (status === 'parsed' || status === 'normalized') return 'ok';
  return 'warn';
}

function llmLabelFor(status: string): string {
  if (status === 'missing_key') return 'missing key';
  return status.replace(/_/g, ' ');
}
