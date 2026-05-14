import { useEffect, useState } from 'react';
import {
  findScope,
  ministerViews,
  scopeConfigs,
  type MinisterViewKey,
  type ScopeKey,
} from '../dashboard/scopes';

const SelectedScopeStorageKey = 'rimai.dashboard.selectedScope';
const SelectedViewStorageKey = 'rimai.dashboard.selectedView';

export interface DashboardSelection {
  selectedScope: ScopeKey;
  selectedView: MinisterViewKey;
  selectScope: (scope: ScopeKey) => void;
  selectView: (view: MinisterViewKey) => void;
}

export function useDashboardSelection(): DashboardSelection {
  const [selectedScope, setSelectedScope] = useState<ScopeKey>(() => readStoredScope());
  const [selectedView, setSelectedView] = useState<MinisterViewKey>(() => readStoredView());

  useEffect(() => {
    writeStoredValue(SelectedScopeStorageKey, selectedScope);
  }, [selectedScope]);

  useEffect(() => {
    writeStoredValue(SelectedViewStorageKey, selectedView);
  }, [selectedView]);

  const selectScope = (scope: ScopeKey) => {
    setSelectedScope(scope);
    if (findScope(scope).kind === 'minister') {
      setSelectedView('advice');
    }
  };

  return {
    selectedScope,
    selectedView,
    selectScope,
    selectView: setSelectedView,
  };
}

function readStoredScope(): ScopeKey {
  const stored = readStoredValue(SelectedScopeStorageKey);
  return isScopeKey(stored) ? stored : 'system';
}

function readStoredView(): MinisterViewKey {
  const stored = readStoredValue(SelectedViewStorageKey);
  return isMinisterViewKey(stored) ? stored : 'advice';
}

function readStoredValue(key: string): string | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStoredValue(key: string, value: string) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Storage can be unavailable in restricted browser contexts; dashboard state can still be session-only.
  }
}

function isScopeKey(value: string | null): value is ScopeKey {
  return typeof value === 'string' && scopeConfigs.some(scope => scope.key === value);
}

function isMinisterViewKey(value: string | null): value is MinisterViewKey {
  return typeof value === 'string' && ministerViews.some(view => view.key === value);
}
