import { useEffect, useState } from 'react';
import {
  findScope,
  ministerViews,
  scopeConfigs,
  type MinisterViewKey,
  type ScopeKey,
} from '../dashboard/scopes';

const SelectedScopeStorageKey = 'rimbob.dashboard.selectedScope';
const SelectedViewStorageKey = 'rimbob.dashboard.selectedView';
const ScopeQueryKey = 'scope';
const ViewQueryKey = 'view';

export interface DashboardSelection {
  selectedScope: ScopeKey;
  selectedView: MinisterViewKey;
  selectScope: (scope: ScopeKey) => void;
  selectView: (view: MinisterViewKey) => void;
}

export function useDashboardSelection(): DashboardSelection {
  const [selectedScope, setSelectedScope] = useState<ScopeKey>(() => readInitialSelection().scope);
  const [selectedView, setSelectedView] = useState<MinisterViewKey>(() => readInitialSelection().view);

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

function readInitialSelection(): { scope: ScopeKey; view: MinisterViewKey } {
  return {
    scope: readQueryScope() ?? readStoredScope(),
    view: readQueryView() ?? readStoredView(),
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

function readQueryValue(key: string): string | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    return new URLSearchParams(window.location.search).get(key);
  } catch {
    return null;
  }
}

function readQueryScope(): ScopeKey | null {
  const queryValue = readQueryValue(ScopeQueryKey);
  return isScopeKey(queryValue) ? queryValue : null;
}

function readQueryView(): MinisterViewKey | null {
  const queryValue = readQueryValue(ViewQueryKey);
  return isMinisterViewKey(queryValue) ? queryValue : null;
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
