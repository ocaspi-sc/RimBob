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
type SelectionUrlMode = 'push' | 'replace';

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
    if (typeof window === 'undefined') {
      return undefined;
    }

    writeSelectionUrl(selectedScope, selectedView, 'replace');

    const handlePopState = () => {
      const nextSelection = readInitialSelection();
      setSelectedScope(nextSelection.scope);
      setSelectedView(nextSelection.view);
    };

    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, []);

  useEffect(() => {
    writeStoredValue(SelectedScopeStorageKey, selectedScope);
  }, [selectedScope]);

  useEffect(() => {
    writeStoredValue(SelectedViewStorageKey, selectedView);
  }, [selectedView]);

  const selectScope = (scope: ScopeKey) => {
    const nextScope = findScope(scope);
    const nextView = defaultViewForScope(nextScope);

    setSelectedScope(nextScope.key);
    setSelectedView(nextView);
    writeSelectionUrl(nextScope.key, nextView, 'push');
  };

  const selectView = (view: DashboardViewKey) => {
    const nextScope = findScope(selectedScope);
    const nextView = viewForScope(nextScope, view);

    setSelectedView(nextView);
    writeSelectionUrl(nextScope.key, nextView, 'push');
  };

  return {
    selectedScope,
    selectedView,
    selectScope,
    selectView,
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

function writeSelectionUrl(scope: ScopeKey, view: DashboardViewKey, mode: SelectionUrlMode) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    const url = new URL(window.location.href);
    url.searchParams.set(ScopeQueryKey, scope);
    url.searchParams.set(ViewQueryKey, view);

    const nextUrl = `${url.pathname}${url.search}${url.hash}`;
    const currentUrl = `${window.location.pathname}${window.location.search}${window.location.hash}`;

    if (nextUrl !== currentUrl) {
      if (mode === 'push') {
        window.history.pushState({}, '', nextUrl);
      } else {
        window.history.replaceState(window.history.state, '', nextUrl);
      }
    }
  } catch {
    // URL history can be unavailable in restricted browser contexts; storage still preserves the selection.
  }
}

function isScopeKey(value: string | null): value is ScopeKey {
  return typeof value === 'string' && scopeConfigs.some(scope => scope.key === value);
}
