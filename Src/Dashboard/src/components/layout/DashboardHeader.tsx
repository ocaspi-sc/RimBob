import type { RimAIStatus } from '../../types/status';
import type { StreamDiagnostics } from '../../types/system';
import { StatusPill } from '../shared/StatusPill';

export function DashboardHeader({
  status,
  stream,
}: {
  status: RimAIStatus | null;
  stream: StreamDiagnostics;
}) {
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
        <h1>RimAI Dashboard v2</h1>
      </div>
      <div className="header-status">
        <StatusPill tone={status ? 'ok' : 'idle'}>Host {status?.server ?? 'checking'}</StatusPill>
        <StatusPill tone={status?.rimapi_reachable ? 'ok' : 'warn'}>RIMAPI {status?.rimapi_reachable ? 'live' : 'waiting'}</StatusPill>
        <StatusPill tone={status?.llm_configured ? 'ok' : 'error'}>LLM {status?.llm_configured ? 'ready' : 'missing key'}</StatusPill>
        <StatusPill tone={stream.state === 'open' ? 'ok' : stream.state === 'error' ? 'warn' : 'idle'}>SSE {stream.state}</StatusPill>
        <StatusPill tone={mayorTone}>Mayor {status?.mayor_running ? 'running' : status?.mayor_last_error ? 'error' : 'idle'}</StatusPill>
      </div>
    </header>
  );
}
