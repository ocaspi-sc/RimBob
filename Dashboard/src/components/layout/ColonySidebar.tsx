import { useMemo, useState } from 'react';
import type { ColonySnapshot, PawnLine } from '../../types/colony';
import type { AdviceItem, AgentFlag } from '../../types/advice';
import type { AssistedApplyAttempt, CabinetRunLogSnapshot, ColonySnapshotMetadata, MinisterTrace } from '../../types/system';
import { pawnPortraitUrl } from '../../api/icons';
import { deriveLogEntries } from '../../dashboard/selectors';
import { iconForField, iconForSection } from '../../dashboard/semanticIcons';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { MetricCard } from '../shared/MetricCard';
import { ResourceQuantity, formatResourceLabel } from '../shared/ResourceQuantity';
import { SemanticLabel } from '../shared/SemanticIcon';
import { SidebarLog } from './SidebarLog';

type SidebarTab = 'colony' | 'log';
const SidebarLogClearedAtKey = 'rimbob.dashboard.sidebarLogClearedAt';
const SidebarLogExpandedIdsKey = 'rimbob.dashboard.sidebarLogExpandedIds';

export function ColonySidebar({
  activeAdvice,
  applyAttempts,
  cabinetRuns,
  criticalAdvice,
  error,
  loadedAt,
  snapshot,
  staleSnapshot,
  traces,
  flags,
}: {
  activeAdvice: AdviceItem[];
  applyAttempts: AssistedApplyAttempt[];
  cabinetRuns: CabinetRunLogSnapshot[];
  criticalAdvice: AdviceItem[];
  error: string | null;
  loadedAt: string | null;
  snapshot: ColonySnapshot | null;
  staleSnapshot: ColonySnapshotMetadata | null;
  traces: MinisterTrace[];
  flags: Record<string, AgentFlag[]>;
}) {
  const [selectedTab, setSelectedTab] = useState<SidebarTab>('log');
  const [logClearedAt, setLogClearedAt] = useState<string | null>(() => readSessionValue(SidebarLogClearedAtKey));
  const [expandedLogIds, setExpandedLogIds] = useState<Set<string>>(() => readSessionSet(SidebarLogExpandedIdsKey));
  const allLogEntries = useMemo(
    () => deriveLogEntries({ activeAdvice, applyAttempts, cabinetRuns, criticalAdvice, flags, traces }),
    [activeAdvice, applyAttempts, cabinetRuns, criticalAdvice, flags, traces],
  );
  const logEntries = useMemo(
    () => allLogEntries.filter(entry => isAfterClearTime(entry.at, logClearedAt)),
    [allLogEntries, logClearedAt],
  );
  const clearLog = () => {
    const nextClearedAt = new Date().toISOString();
    setLogClearedAt(nextClearedAt);
    writeSessionValue(SidebarLogClearedAtKey, nextClearedAt);
  };
  const updateExpandedLogIds = (nextExpandedIds: Set<string>) => {
    setExpandedLogIds(nextExpandedIds);
    writeSessionValue(SidebarLogExpandedIdsKey, JSON.stringify([...nextExpandedIds]));
  };

  return (
    <aside className="colony-sidebar panel-shell">
      <div className="sidebar-tabs" role="tablist" aria-label="Right sidebar">
        <button
          aria-controls="sidebar-colony-panel"
          aria-selected={selectedTab === 'colony'}
          className={`sidebar-tab ${selectedTab === 'colony' ? 'active' : ''}`}
          id="sidebar-colony-tab"
          role="tab"
          type="button"
          onClick={() => setSelectedTab('colony')}
        >
          COLONY
        </button>
        <button
          aria-controls="sidebar-log-panel"
          aria-selected={selectedTab === 'log'}
          className={`sidebar-tab ${selectedTab === 'log' ? 'active' : ''}`}
          id="sidebar-log-tab"
          role="tab"
          type="button"
          onClick={() => setSelectedTab('log')}
        >
          LOG
        </button>
      </div>

      <section
        aria-labelledby="sidebar-colony-tab"
        className="sidebar-tab-panel"
        hidden={selectedTab !== 'colony'}
        id="sidebar-colony-panel"
        role="tabpanel"
      >
        <ColonySidebarBody
          error={error}
          loadedAt={loadedAt}
          snapshot={snapshot}
          staleSnapshot={staleSnapshot}
        />
      </section>
      <section
        aria-labelledby="sidebar-log-tab"
        className="sidebar-tab-panel sidebar-log-panel"
        hidden={selectedTab !== 'log'}
        id="sidebar-log-panel"
        role="tabpanel"
      >
        <SidebarLog
          clearedAt={logClearedAt}
          entries={logEntries}
          expandedIds={expandedLogIds}
          onClear={clearLog}
          onExpandedIdsChange={updateExpandedLogIds}
        />
      </section>
    </aside>
  );
}

function isAfterClearTime(entryAt: string, clearedAt: string | null): boolean {
  if (!clearedAt) return true;

  const entryTime = new Date(entryAt).getTime();
  const clearTime = new Date(clearedAt).getTime();
  if (Number.isNaN(entryTime) || Number.isNaN(clearTime)) return true;

  return entryTime > clearTime;
}

function readSessionValue(key: string): string | null {
  if (typeof window === 'undefined') return null;

  try {
    return window.sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function readSessionSet(key: string): Set<string> {
  const rawValue = readSessionValue(key);
  if (!rawValue) return new Set();

  try {
    const parsed = JSON.parse(rawValue);
    if (!Array.isArray(parsed)) return new Set();

    return new Set(parsed.filter((item): item is string => typeof item === 'string'));
  } catch {
    return new Set();
  }
}

function writeSessionValue(key: string, value: string) {
  if (typeof window === 'undefined') return;

  try {
    window.sessionStorage.setItem(key, value);
  } catch {
    // Session storage can be unavailable in restricted browser contexts; in-memory state still clears the visible log.
  }
}

function ColonySidebarBody({
  error,
  loadedAt,
  snapshot,
  staleSnapshot,
}: {
  error: string | null;
  loadedAt: string | null;
  snapshot: ColonySnapshot | null;
  staleSnapshot: ColonySnapshotMetadata | null;
}) {
  if (error && !snapshot) {
    return <EmptyState code="COLONY SNAPSHOT FAILED">{error}</EmptyState>;
  }

  if (!snapshot) {
    return <EmptyState code="COLONY SNAPSHOT">Waiting for briefing data.</EmptyState>;
  }

  const pawns = snapshot.colonists.pawns ?? [];

  return (
    <>
      <header className="sidebar-header">
        <div>
          <span className="eyebrow">Colony</span>
          <h2>{formatDate(snapshot)}</h2>
        </div>
        <small>day {formatTotalDays(snapshot.date.totalDays)} | tick {snapshot.gameTick.toLocaleString()}</small>
      </header>

      <div className="sidebar-metrics">
        <MetricCard
          label={<SemanticLabel icon={iconForField('food')}><span>Food</span></SemanticLabel>}
          value={formatFoodDays(snapshot.food.estimatedDaysOfFood)}
          note={formatLatentFoodDays(snapshot.food.latentFoodDays)}
          tone={snapshot.food.estimatedDaysOfFood != null && snapshot.food.estimatedDaysOfFood < 7 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label={<SemanticLabel icon={iconForField('mood')}><span>Mood</span></SemanticLabel>}
          value={`${Math.round(snapshot.mood.averageMood * 100)}%`}
          tone={snapshot.mood.breakRiskCount > 0 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label={<SemanticLabel icon={iconForField('power')}><span>Power</span></SemanticLabel>}
          value={`${snapshot.power.netW >= 0 ? '+' : ''}${Math.round(snapshot.power.netW)} W`}
          tone={snapshot.power.netW < 0 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label={<SemanticLabel icon={iconForField('threat')}><span>Threat</span></SemanticLabel>}
          value={snapshot.threat.activeRaid ? 'raid' : 'clear'}
          tone={snapshot.threat.activeRaid ? 'error' : 'neutral'}
        />
      </div>

      <div className="sidebar-strip">
        <span>{snapshot.season.currentSeason ?? 'season unknown'}</span>
        <span>{snapshot.weather.temperatureC.toFixed(1)} C</span>
        <span>{snapshot.research.currentProject ?? 'no research'}</span>
      </div>

      <ResourceIconRows snapshot={snapshot} />

      <section className="colonist-panel">
        <div className="colonist-title">
          <h3><SemanticLabel icon={iconForSection('colonists')}><span>Colonists</span></SemanticLabel></h3>
          <span>{snapshot.colonists.count}</span>
        </div>
        {pawns.length === 0 ? (
          <div className="muted-row">Pawn detail is not present in this snapshot.</div>
        ) : (
          <div className="colonist-list">
            {pawns.map(pawn => <ColonistCard key={pawn.id} pawn={pawn} />)}
          </div>
        )}
      </section>

      <footer className={`sidebar-footer ${error ? 'warn' : ''}`}>
        {staleSnapshot && (
          <strong className="snapshot-stale-chip" role="status">
            SNAPSHOT - stale{staleSnapshot.captured_at ? ` (captured ${formatSnapshotCaptured(staleSnapshot.captured_at)})` : ''}
          </strong>
        )}
        <span>briefing v{snapshot.briefingVersion}{loadedAt ? ` | updated ${formatLoadedAt(loadedAt)}` : ''}</span>
        {error && (
          <strong role="status">Snapshot poll failed; showing last successful data.</strong>
        )}
      </footer>
    </>
  );
}

function ColonistCard({ pawn }: { pawn: PawnLine }) {
  return (
    <article className={`colonist-card ${pawn.isDowned ? 'warn' : ''}`}>
      <div className="colonist-card-main">
        <GameIcon
          fallback={initials(pawn.name)}
          label={`${pawn.name} portrait`}
          size="md"
          src={pawnPortraitUrl(pawn.id)}
        />
        <div>
          <h4>{pawn.name}</h4>
          <small>{pawn.currentJob ?? 'no current job'}</small>
        </div>
      </div>
      <div className="colonist-stats">
        <span>Mood {Math.round(pawn.mood * 100)}%</span>
        <span>Health {Math.round(pawn.health * 100)}%</span>
        <span>Hunger {Math.round(pawn.hunger * 100)}%</span>
      </div>
      <footer>
        <span>{pawn.topSkill ?? `Age ${pawn.age}`}</span>
        {pawn.isDowned && <strong>Downed</strong>}
      </footer>
    </article>
  );
}

function ResourceIconRows({ snapshot }: { snapshot: ColonySnapshot }) {
  const rows = [
    ...(snapshot.food.cropBreakdown ?? [])
      .filter(crop => crop.count > 0)
      .slice(0, 3)
      .map(crop => ({ def: crop.def, count: crop.count, label: 'Crop' })),
    ...topResourceRows(snapshot.resources?.materials ?? {}, 'Material', 3),
    ...topResourceRows(snapshot.resources?.medicine ?? {}, 'Medicine', 1),
    ...topResourceRows(snapshot.resources?.weapons ?? {}, 'Weapon', 1),
  ].slice(0, 7);

  if (rows.length === 0) return null;

  return (
    <section className="resource-icon-panel">
      <div className="resource-icon-title">
        <SemanticLabel icon={iconForSection('resources')}><span>Resources</span></SemanticLabel>
        <small>{rows.length}</small>
      </div>
      <div className="resource-icon-list">
        {rows.map(row => (
          <div className="resource-icon-row" key={`${row.label}-${row.def}`}>
            <ResourceQuantity defName={row.def} label={formatResourceLabel(row.def)} quantity={row.count} />
          </div>
        ))}
      </div>
    </section>
  );
}

function topResourceRows(values: Record<string, number>, label: string, limit: number) {
  return Object.entries(values)
    .filter(([, count]) => count > 0)
    .sort(([, left], [, right]) => right - left)
    .slice(0, limit)
    .map(([def, count]) => ({ def, count, label }));
}

function initials(name: string): string {
  const trimmed = name.trim();
  if (!trimmed) return '?';
  return trimmed.slice(0, 1).toUpperCase();
}

function formatDate(snapshot: ColonySnapshot): string {
  return snapshot.date.label || snapshot.date.rawRimWorldDate;
}

function formatTotalDays(value: number): string {
  if (!Number.isFinite(value)) return '?';
  return value.toFixed(value >= 10 ? 0 : 1);
}

function formatFoodDays(value: number | null): string {
  return value != null ? `${value.toFixed(1)}d edible` : 'unknown';
}

function formatLatentFoodDays(value: number | null): string | undefined {
  if (value == null || value < 0.05) return undefined;
  return `+${value.toFixed(1)}d behind forbidden`;
}

function formatLoadedAt(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function formatSnapshotCaptured(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}
