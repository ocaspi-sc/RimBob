import type { ColonySnapshot, PawnLine } from '../../types/colony';
import { itemIconUrl, pawnPortraitUrl } from '../../api/icons';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { MetricCard } from '../shared/MetricCard';

export function ColonySidebar({
  error,
  loadedAt,
  snapshot,
}: {
  error: string | null;
  loadedAt: string | null;
  snapshot: ColonySnapshot | null;
}) {
  if (error && !snapshot) {
    return (
      <aside className="colony-sidebar panel-shell">
        <EmptyState code="COLONY SNAPSHOT FAILED">{error}</EmptyState>
      </aside>
    );
  }

  if (!snapshot) {
    return (
      <aside className="colony-sidebar panel-shell">
        <EmptyState code="COLONY SNAPSHOT">Waiting for briefing data.</EmptyState>
      </aside>
    );
  }

  const pawns = snapshot.colonists.pawns ?? [];

  return (
    <aside className="colony-sidebar panel-shell">
      <header className="sidebar-header">
        <div>
          <span className="eyebrow">Colony</span>
          <h2>{formatDate(snapshot)}</h2>
        </div>
        <small>tick {snapshot.gameTick.toLocaleString()}</small>
      </header>

      <div className="sidebar-metrics">
        <MetricCard
          label="🍲 Food"
          value={snapshot.food.estimatedDaysOfFood != null ? `${snapshot.food.estimatedDaysOfFood.toFixed(1)}d` : 'unknown'}
          tone={snapshot.food.estimatedDaysOfFood != null && snapshot.food.estimatedDaysOfFood < 7 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label="🙂 Mood"
          value={`${Math.round(snapshot.mood.averageMood * 100)}%`}
          tone={snapshot.mood.breakRiskCount > 0 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label="⚡ Power"
          value={`${snapshot.power.netW >= 0 ? '+' : ''}${Math.round(snapshot.power.netW)} W`}
          tone={snapshot.power.netW < 0 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label="🛡️ Threat"
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
          <h3>👥 Colonists</h3>
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
        <span>briefing v{snapshot.briefingVersion}{loadedAt ? ` | updated ${formatLoadedAt(loadedAt)}` : ''}</span>
        {error && (
          <strong role="status">Snapshot poll failed; showing last successful data.</strong>
        )}
      </footer>
    </aside>
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
        <span>Resources</span>
        <small>{rows.length}</small>
      </div>
      <div className="resource-icon-list">
        {rows.map(row => (
          <div className="resource-icon-row" key={`${row.label}-${row.def}`}>
            <GameIcon
              fallback={row.def.slice(0, 1).toUpperCase()}
              label={`${row.def} icon`}
              size="xs"
              src={itemIconUrl(row.def)}
            />
            <span>{formatDef(row.def)}</span>
            <strong>{row.count.toLocaleString()}</strong>
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

function formatDef(value: string): string {
  return value
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2');
}

function formatDate(snapshot: ColonySnapshot): string {
  const date = snapshot.date;
  if (date.year != null && date.quadrum && date.day != null) {
    return `${date.quadrum} ${date.day}, Y${date.year}`;
  }
  return date.raw;
}

function formatLoadedAt(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}
