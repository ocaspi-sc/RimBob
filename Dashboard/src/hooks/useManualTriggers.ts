import { useCallback, useState } from 'react';
import { triggerCabinet } from '../api/cabinet';
import { triggerMinisterLlm, triggerMinisterRules as postMinisterRules } from '../api/ministers';
import type { ScopeConfig, ScopeKey } from '../dashboard/scopes';

export type TriggerTarget = 'cabinet' | `${ScopeKey}:rules` | `${ScopeKey}:llm`;

export interface TriggerState {
  target: TriggerTarget | null;
  error: string | null;
}

export interface ManualTriggers {
  triggerState: TriggerState;
  triggerCabinetNow: () => Promise<void>;
  triggerMinisterLlm: (scope: ScopeConfig) => Promise<void>;
  triggerMinisterRules: (scope: ScopeConfig) => Promise<void>;
}

export function useManualTriggers(): ManualTriggers {
  const [triggerState, setTriggerState] = useState<TriggerState>({ target: null, error: null });

  const runManualTrigger = useCallback(async (
    target: TriggerTarget,
    label: string,
    action: () => Promise<unknown>,
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

  const triggerCabinetNow = useCallback(
    async () => runManualTrigger('cabinet', 'Run Cabinet Now', () => triggerCabinet()),
    [runManualTrigger],
  );

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

  return {
    triggerState,
    triggerCabinetNow,
    triggerMinisterLlm: runMinisterLlm,
    triggerMinisterRules: runMinisterRules,
  };
}
