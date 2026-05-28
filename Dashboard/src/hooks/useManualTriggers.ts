import { useCallback, useState } from 'react';
import { triggerCabinet } from '../api/cabinet';
import { triggerMinister } from '../api/ministers';
import type { ScopeConfig, ScopeKey } from '../dashboard/scopes';

export type TriggerTarget = 'cabinet' | ScopeKey;

export interface TriggerState {
  target: TriggerTarget | null;
  error: string | null;
}

export interface ManualTriggers {
  triggerState: TriggerState;
  triggerCabinetNow: () => Promise<void>;
  triggerMinisterNow: (scope: ScopeConfig) => Promise<void>;
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

  const runMinisterTrigger = useCallback(
    async (scope: ScopeConfig, label: string) =>
      runManualTrigger(scope.key, label, () => triggerMinister(scope.key)),
    [runManualTrigger],
  );

  const triggerMinisterNow = useCallback(
    async (scope: ScopeConfig) => runMinisterTrigger(scope, `Run ${scope.label} Now`),
    [runMinisterTrigger],
  );

  const triggerMinisterRules = useCallback(
    async (scope: ScopeConfig) => runMinisterTrigger(scope, 'Run Rules'),
    [runMinisterTrigger],
  );

  return {
    triggerState,
    triggerCabinetNow,
    triggerMinisterNow,
    triggerMinisterRules,
  };
}
