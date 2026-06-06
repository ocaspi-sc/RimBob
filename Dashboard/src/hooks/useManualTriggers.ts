import { useCallback, useEffect, useRef, useState } from 'react';
import { triggerCabinet, triggerCabinetRules } from '../api/cabinet';
import { triggerMinisterLlm, triggerMinisterRules as postMinisterRules } from '../api/ministers';
import type { ScopeConfig, ScopeKey } from '../dashboard/scopes';
import type { CabinetRunLogSnapshot, CabinetRunStepSnapshot } from '../types/system';

export type TriggerTarget = 'cabinet' | 'cabinet_rules' | `${ScopeKey}:rules` | `${ScopeKey}:llm`;

export interface TriggerState {
  target: TriggerTarget | null;
  error: string | null;
}

export interface CabinetRunDialogState {
  open: boolean;
  run: CabinetRunLogSnapshot | null;
}

export interface ManualTriggers {
  triggerState: TriggerState;
  cabinetRunDialog: CabinetRunDialogState;
  triggerCabinetNow: () => Promise<void>;
  triggerCabinetRulesOnly: () => Promise<void>;
  triggerMinisterLlm: (scope: ScopeConfig) => Promise<void>;
  triggerMinisterRules: (scope: ScopeConfig) => Promise<void>;
  closeCabinetRunDialog: () => void;
}

export function useManualTriggers(cabinetRunEvents: CabinetRunLogSnapshot[] = []): ManualTriggers {
  const [triggerState, setTriggerState] = useState<TriggerState>({ target: null, error: null });
  const [cabinetRunDialog, setCabinetRunDialog] = useState<CabinetRunDialogState>({ open: false, run: null });
  const activeCabinetRunIdRef = useRef<string | null>(null);

  useEffect(() => {
    const activeRunId = activeCabinetRunIdRef.current;
    if (!activeRunId) return;

    const matchingRun = cabinetRunEvents.find(run => run.run_id === activeRunId);
    if (!matchingRun) return;

    setCabinetRunDialog(current => ({
      open: current.open,
      run: matchingRun,
    }));
  }, [cabinetRunEvents]);

  const runManualTrigger = useCallback(async (
    target: TriggerTarget,
    label: string,
    action: () => Promise<unknown>,
    onAccepted?: () => void,
  ) => {
    let shouldRun = true;
    setTriggerState(current => {
      if (current.target !== null) {
        shouldRun = false;
        return current;
      }

      return { target, error: null };
    });

    if (!shouldRun) return;
    onAccepted?.();

    try {
      await action();
      setTriggerState({ target: null, error: null });
    } catch (error) {
      setTriggerState({
        target: null,
        error: `${label} failed: ${error instanceof Error ? error.message : String(error)}`,
      });
    }
  }, []);

  const triggerCabinetNow = useCallback(async () => {
    const runId = createRunId();
    const seededRun = seedCabinetRun(runId, 'cabinet');
    await runManualTrigger(
      'cabinet',
      'Run Cabinet Now',
      async () => {
        try {
          const result = await triggerCabinet(runId);
          if (result.run_log) {
            activeCabinetRunIdRef.current = result.run_log.run_id;
            setCabinetRunDialog(current => ({
              open: current.open,
              run: result.run_log ?? current.run,
            }));
          }
        } catch (error) {
          const message = error instanceof Error ? error.message : String(error);
          setCabinetRunDialog(current => ({
            open: current.open,
            run: failCabinetRun(current.run ?? seededRun, message),
          }));
          throw error;
        }
      },
      () => {
        activeCabinetRunIdRef.current = runId;
        setCabinetRunDialog({ open: true, run: seededRun });
      },
    );
  }, [runManualTrigger]);

  const triggerCabinetRulesOnly = useCallback(async () => {
    const runId = createRunId();
    const seededRun = seedCabinetRun(runId, 'cabinet_rules');
    await runManualTrigger(
      'cabinet_rules',
      'Run Cabinet (Rules Only)',
      async () => {
        try {
          const result = await triggerCabinetRules(runId);
          if (result.run_log) {
            activeCabinetRunIdRef.current = result.run_log.run_id;
            setCabinetRunDialog(current => ({
              open: current.open,
              run: result.run_log ?? current.run,
            }));
          }
        } catch (error) {
          const message = error instanceof Error ? error.message : String(error);
          setCabinetRunDialog(current => ({
            open: current.open,
            run: failCabinetRun(current.run ?? seededRun, message),
          }));
          throw error;
        }
      },
      () => {
        activeCabinetRunIdRef.current = runId;
        setCabinetRunDialog({ open: true, run: seededRun });
      },
    );
  }, [runManualTrigger]);

  const runMinisterTrigger = useCallback((
    scope: ScopeConfig,
    mode: 'rules' | 'llm',
    label: string,
    action: () => Promise<unknown>,
  ) =>
    runManualTrigger(`${scope.key}:${mode}`, label, action),
    [runManualTrigger],
  );

  const runMinisterLlm = useCallback(
    async (scope: ScopeConfig) =>
      runMinisterTrigger(scope, 'llm', 'Run LLM', () => triggerMinisterLlm(scope.key)),
    [runMinisterTrigger],
  );

  const runMinisterRules = useCallback(
    async (scope: ScopeConfig) =>
      runMinisterTrigger(scope, 'rules', 'Run Rules', () => postMinisterRules(scope.key)),
    [runMinisterTrigger],
  );

  const closeCabinetRunDialog = useCallback(() => {
    setCabinetRunDialog(current => ({ ...current, open: false }));
  }, []);

  return {
    triggerState,
    cabinetRunDialog,
    triggerCabinetNow,
    triggerCabinetRulesOnly,
    triggerMinisterLlm: runMinisterLlm,
    triggerMinisterRules: runMinisterRules,
    closeCabinetRunDialog,
  };
}

function createRunId(): string {
  if ('crypto' in window && typeof window.crypto.randomUUID === 'function') {
    return window.crypto.randomUUID();
  }

  return `cabinet-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function seedCabinetRun(runId: string, scope: 'cabinet' | 'cabinet_rules'): CabinetRunLogSnapshot {
  const now = new Date().toISOString();
  const requestStep: CabinetRunStepSnapshot = {
    key: 'request_sent',
    label: 'Request sent',
    kind: 'request',
    status: 'running',
    started_at: now,
    completed_at: null,
    duration_ms: null,
    detail: 'Dashboard sent the manual cabinet run request.',
    minister: null,
    state_source: null,
    used_restored_snapshot: null,
    trace_path: null,
    rule_fired: null,
    escalation_reason: null,
    advice_count: null,
    flag_count: null,
    trace_note: null,
    error_type: null,
    error_message: null,
    children: [],
  };

  return {
    run_id: runId,
    scope,
    trigger: 'ManualTrigger',
    status: 'running',
    started_at: now,
    completed_at: null,
    duration_ms: null,
    steps: [requestStep],
    state_source: null,
    used_restored_snapshot: null,
    error_type: null,
    error_message: null,
  };
}

function failCabinetRun(run: CabinetRunLogSnapshot, message: string): CabinetRunLogSnapshot {
  const now = new Date().toISOString();
  const failedStep: CabinetRunStepSnapshot = {
    key: 'cabinet_failed',
    label: 'Cabinet failed',
    kind: 'complete',
    status: 'failed',
    started_at: now,
    completed_at: now,
    duration_ms: 0,
    detail: message,
    minister: null,
    state_source: run.state_source,
    used_restored_snapshot: run.used_restored_snapshot,
    trace_path: null,
    rule_fired: null,
    escalation_reason: null,
    advice_count: null,
    flag_count: null,
    trace_note: null,
    error_type: 'DashboardRequestError',
    error_message: message,
    children: [],
  };

  return {
    ...run,
    status: 'failed',
    completed_at: now,
    duration_ms: durationMs(run.started_at, now),
    error_type: failedStep.error_type,
    error_message: message,
    steps: [
      ...run.steps.filter(step => step.key !== failedStep.key),
      failedStep,
    ],
  };
}

function durationMs(startedAt: string, completedAt: string): number {
  const started = new Date(startedAt).getTime();
  const completed = new Date(completedAt).getTime();
  if (Number.isNaN(started) || Number.isNaN(completed)) return 0;
  return Math.max(0, Math.round(completed - started));
}
