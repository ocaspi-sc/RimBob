import { useEffect, useState } from 'react';
import {
  defaultViewForScope,
  findScope,
  isDashboardViewKey,
  scopeConfigs,
  viewForScope,
  type DashboardViewKey,
  type ScopeKey,
} from '../dashboard/scopes';

const SelectedScopeStorageKey = 'rimbob.dashboard.selectedScope';
const SelectedViewStorageKey = 'rimbob.dashboard.selectedView';
const ScopeQueryKey = 'scope';
const ViewQueryKey = 'view';

export interface DashboardSelection {
  selectedScope: ScopeKey;
  selectedView: DashboardViewKey;
  selectScope: (scope: ScopeKey) => void;
  selectView: (view: DashboardViewKey) => void;
}

export function useDashboardSelection(): DashboardSelection {
  const initialSelection = readInitialSelection();
  const [selectedScope, setSelectedScope] = useState<ScopeKey>(() => initialSelection.scope);
  const [selectedView, setSelectedView] = useState<DashboardViewKey>(() => initialSelection.view);

  useEffect(() => {
    writeStoredValue(SelectedScopeStorageKey, selectedScope);
  }, [selectedScope]);

  useEffect(() => {
    writeStoredValue(SelectedViewStorageKey, selectedView);
  }, [selectedView]);

  const selectScope = (scope: ScopeKey) => {
    setSelectedScope(scope);
    setSelectedView(defaultViewForScope(findScope(scope)));
  };

  return {
    selectedScope,
    selectedView,
    selectScope,
    selectView: view => setSelectedView(viewForScope(findScope(selectedScope), view)),
  };
}

function readInitialSelection(): { scope: ScopeKey; view: DashboardViewKey } {
  const scope = readQueryScope() ?? readStoredScope();
  const view = readQueryView() ?? readStoredView();

  return {
    scope,
    view: viewForScope(findScope(scope), view),
  };
}

function readStoredScope(): ScopeKey {
  const stored = readStoredValue(SelectedScopeStorageKey);
  return isScopeKey(stored) ? stored : 'system';
}

function readStoredView(): DashboardViewKey {
  const stored = readStoredValue(SelectedViewStorageKey);
  return isDashboardViewKey(stored) ? stored : 'runtime';
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

function readQueryView(): DashboardViewKey | null {
  const queryValue = readQueryValue(ViewQueryKey);
  return isDashboardViewKey(queryValue) ? queryValue : null;
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
