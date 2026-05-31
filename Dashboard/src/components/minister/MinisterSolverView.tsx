import { fetchSolver, type WillieSolverOutputPayload, type WillieSolverPayload } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { isScopeMinister } from '../../dashboard/selectors';
import { iconForField, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { MinisterTrace, SystemHealth } from '../../types/system';
import { EmptyState } from '../shared/EmptyState';
import { MetricCard } from '../shared/MetricCard';
import { SemanticLabel } from '../shared/SemanticIcon';
import { StatusPill, type PillTone } from '../shared/StatusPill';
import { readinessTone } from './readiness';

type FunnelStageId = 'anchors' | 'drafts' | 'hard_gate' | 'reachable' | 'validated' | 'options';
type FunnelStageState = 'passed' | 'failed' | 'pending' | 'unknown';

const funnelStages: Array<{ id: FunnelStageId; label: string }> = [
  { id: 'anchors', label: 'anchors' },
  { id: 'drafts', label: 'drafts' },
  { id: 'hard_gate', label: 'hard-gate' },
  { id: 'reachable', label: 'reachable' },
  { id: 'validated', label: 'validated' },
  { id: 'options', label: 'options' },
];

const noFitStage: Record<string, FunnelStageId> = {
  NoAnchors: 'anchors',
  NoDrafts: 'drafts',
  HardGateRejected: 'hard_gate',
  NoReachablePath: 'reachable',
  ValidationRejected: 'validated',
};

export function MinisterSolverView({
  scope,
  systemHealth,
}: {
  scope: ScopeConfig;
  systemHealth: SystemHealth | null;
}) {
  const latestTrace = findTrace(systemHealth, scope);
  const traceKey = latestTrace
    ? `${latestTrace.startedAt}|${latestTrace.completedAt ?? ''}|${latestTrace.status}`
    : 'no-trace';
  const solver = useAsyncResource<WillieSolverPayload | null>(
    signal => scope.key === 'willie'
      ? fetchSolver(scope.key, signal)
      : Promise.resolve(null),
    [scope.key, traceKey],
  );

  if (scope.key !== 'willie') {
    return <EmptyState code="SOLVER NOT WIRED">Solver is a Willie-only construction view.</EmptyState>;
  }

  if (solver.loading) {
    return <EmptyState code="SOLVER">Loading latest placement solver outcome.</EmptyState>;
  }

  if (solver.error || !solver.data) {
    return (
      <EmptyState code="SOLVER UNAVAILABLE">
        {solver.error ?? 'Willie solver endpoint returned no payload.'}
      </EmptyState>
    );
  }

  const payload = solver.data;
  const output = outputOf(payload);
  const status = output.status;

  return (
    <div className="minister-view willie-solver-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2><SemanticLabel icon={iconForView('solver')}><span>Solver</span></SemanticLabel></h2>
        <p>Latest Placement Solver outcome, no-fit stage, and readiness gates.</p>
      </header>

      {status === 'not_seen_yet' ? (
        <EmptyState code="NO SOLVE YET">Willie has not run the Placement Solver since this Host process started.</EmptyState>
      ) : (
        <>
          <SolverSummary payload={payload} output={output} />
          <PipelineFunnel output={output} />
          <ReadinessLadder output={output} />
        </>
      )}
    </div>
  );
}

function SolverSummary({
  output,
  payload,
}: {
  output: WillieSolverOutputPayload;
  payload: WillieSolverPayload;
}) {
  const request = payload.request;
  const statusMeta = statusLabel(output);
  const draftCount = output.trace?.drafts.length ?? 0;

  return (
    <section className="solver-summary">
      <div className="prompt-meta solver-request-meta">
        {request ? (
          <>
            <span>{request.sourceMinister ?? 'unknown source'} to {request.requestedFrom ?? payload.minister}</span>
            <span>{formatLabel(request.targetClass)}</span>
            {request.roomClass && <span>{formatLabel(request.roomClass)}</span>}
            {request.targetDef && <span>{request.targetDef}</span>}
            {request.priority && <span>{formatLabel(request.priority)}</span>}
          </>
        ) : (
          <span>No driving request recorded.</span>
        )}
        {payload.gameTick !== null && <span>tick {formatInteger(payload.gameTick)}</span>}
        <span>{new Date(payload.capturedAt).toLocaleString()}</span>
      </div>

      {request && (
        <div className="solver-request-line">
          <strong>{request.request}</strong>
          <small>{request.reason}</small>
        </div>
      )}

      <div className="metric-grid compact solver-summary-grid">
        <MetricCard
          label={<SemanticLabel icon={iconForField('status')}><span>status</span></SemanticLabel>}
          value={<StatusPill tone={statusTone(output.status)}>{statusMeta.label}</StatusPill>}
          note={statusMeta.note}
          tone={metricTone(output.status)}
        />
        <MetricCard
          label={<SemanticLabel icon={iconForField('selected_rule')}><span>selectedRule</span></SemanticLabel>}
          value={output.trace?.selectedRule ?? '-'}
          note="PlacementTrace.SelectedRule"
        />
        <MetricCard
          label={<SemanticLabel icon={iconForField('trace_count')}><span>drafts</span></SemanticLabel>}
          value={formatInteger(draftCount)}
          note={`${formatInteger(output.trace?.notes.length ?? 0)} trace notes`}
        />
      </div>
    </section>
  );
}

function PipelineFunnel({ output }: { output: WillieSolverOutputPayload }) {
  const states = funnelStates(output);

  return (
    <section className="solver-panel" aria-label="Placement solver pipeline funnel">
      <header>
        <h3><SemanticLabel icon={iconForField('placement_trace')}><span>Pipeline funnel</span></SemanticLabel></h3>
        <StatusPill tone={statusTone(output.status)}>{statusLabel(output).label}</StatusPill>
      </header>
      <div className="solver-funnel">
        {funnelStages.map(stage => (
          <div className={`solver-funnel-node ${states[stage.id]}`} key={stage.id}>
            <span>{stage.label}</span>
            <strong>{stageStateLabel(states[stage.id])}</strong>
          </div>
        ))}
      </div>
    </section>
  );
}

function ReadinessLadder({ output }: { output: WillieSolverOutputPayload }) {
  const gates = [
    ['Draftable', output.draftable],
    ['PlacementValid', output.placementValid],
    ['MaterialsReady', output.materialsReady],
    ['ApplyReady', output.applyReady],
  ] as const;

  return (
    <section className="solver-panel" aria-label="Placement solver readiness ladder">
      <header>
        <h3><SemanticLabel icon={iconForField('materials_ready')}><span>Readiness ladder</span></SemanticLabel></h3>
        <small>Draftable to player-click Apply.</small>
      </header>
      <div className="solver-readiness-ladder">
        {gates.map(([label, value]) => (
          <div className="solver-readiness-gate" key={label}>
            <span>{label}</span>
            <StatusPill tone={readinessTone(value)}>{formatLabel(value ?? 'unknown')}</StatusPill>
          </div>
        ))}
      </div>
    </section>
  );
}

function outputOf(payload: WillieSolverPayload): WillieSolverOutputPayload {
  return payload.output ?? payload;
}

function findTrace(systemHealth: SystemHealth | null, scope: ScopeConfig): MinisterTrace | null {
  return systemHealth?.traces.find(trace => isScopeMinister(trace.minister, scope)) ?? null;
}

function funnelStates(output: WillieSolverOutputPayload): Record<FunnelStageId, FunnelStageState> {
  if (output.status === 'options') {
    return Object.fromEntries(funnelStages.map(stage => [stage.id, 'passed'])) as Record<FunnelStageId, FunnelStageState>;
  }

  if (output.status !== 'no_fit' || !output.noFit) {
    return Object.fromEntries(funnelStages.map(stage => [stage.id, 'unknown'])) as Record<FunnelStageId, FunnelStageState>;
  }

  const failedStage = noFitStage[output.noFit] ?? 'options';
  const failedIndex = funnelStages.findIndex(stage => stage.id === failedStage);

  return Object.fromEntries(funnelStages.map((stage, index) => {
    if (index < failedIndex) return [stage.id, 'passed'];
    if (index === failedIndex) return [stage.id, 'failed'];
    return [stage.id, 'pending'];
  })) as Record<FunnelStageId, FunnelStageState>;
}

function statusLabel(output: WillieSolverOutputPayload): { label: string; note?: string } {
  if (output.status === 'options') {
    const count = output.trace?.drafts.filter(draft => draft.status === 'selected').length ?? 0;
    return {
      label: 'options',
      note: count > 0 ? `${formatInteger(count)} selected draft${count === 1 ? '' : 's'}` : 'validated options emitted',
    };
  }

  if (output.status === 'no_fit') {
    return {
      label: `no-fit: ${formatLabel(output.noFit ?? 'unknown')}`,
      note: 'No validated option reached AdviceItem.options[].',
    };
  }

  if (output.status === 'error') {
    return {
      label: `error: ${output.errorType ?? 'unknown'}`,
      note: output.errorMessage ?? 'Placement solver failed before returning a trace.',
    };
  }

  return { label: output.status.replace(/_/g, ' ') };
}

function statusTone(status: string): PillTone {
  if (status === 'options') return 'ok';
  if (status === 'no_fit') return 'warn';
  if (status === 'error') return 'error';
  return 'idle';
}

function metricTone(status: string): 'neutral' | 'ok' | 'warn' | 'error' {
  if (status === 'options') return 'ok';
  if (status === 'no_fit') return 'warn';
  if (status === 'error') return 'error';
  return 'neutral';
}

function stageStateLabel(state: FunnelStageState): string {
  switch (state) {
    case 'passed':
      return 'passed';
    case 'failed':
      return 'failed';
    case 'pending':
      return 'not reached';
    default:
      return 'unknown';
  }
}

function formatLabel(value: string | null | undefined): string {
  if (!value) return '-';
  return value
    .replace(/_/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function formatInteger(value: number): string {
  return Math.round(value).toLocaleString();
}
