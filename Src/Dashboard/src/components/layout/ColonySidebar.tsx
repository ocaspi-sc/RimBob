import type { ColonySnapshot, PawnLine } from '../../types/colony';
import { EmptyState } from '../shared/EmptyState';
import { MetricCard } from '../shared/MetricCard';

export function ColonySidebar({
  error,
  snapshot,
}: {
  error: string | null;
  snapshot: ColonySnapshot | null;
}) {
  if (error) {
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
          label="Food"
          value={snapshot.food.estimatedDaysOfFood != null ? `${snapshot.food.estimatedDaysOfFood.toFixed(1)}d` : 'unknown'}
          tone={snapshot.food.estimatedDaysOfFood != null && snapshot.food.estimatedDaysOfFood < 7 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label="Mood"
          value={`${Math.round(snapshot.mood.averageMood * 100)}%`}
          tone={snapshot.mood.breakRiskCount > 0 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label="Power"
          value={`${snapshot.power.netW >= 0 ? '+' : ''}${Math.round(snapshot.power.netW)} W`}
          tone={snapshot.power.netW < 0 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label="Threat"
          value={snapshot.threat.activeRaid ? 'raid' : 'clear'}
          tone={snapshot.threat.activeRaid ? 'error' : 'neutral'}
        />
      </div>

      <div className="sidebar-strip">
        <span>{snapshot.season.currentSeason ?? 'season unknown'}</span>
        <span>{snapshot.weather.temperatureC.toFixed(1)} C</span>
        <span>{snapshot.research.currentProject ?? 'no research'}</span>
      </div>

      <section className="colonist-panel">
        <div className="colonist-title">
          <h3>Colonists</h3>
          <span>{snapshot.colonists.count}</span>
        </div>
        {pawns.length === 0 ? (
          <div className="muted-row">Pawn detail is not present in this snapshot.</div>
        ) : (
          <div className="colonist-list">
            {pawns.map(pawn => <ColonistCard key={pawn.name} pawn={pawn} />)}
          </div>
        )}
      </section>

      <footer className="sidebar-footer">
        briefing v{snapshot.briefingVersion}
      </footer>
    </aside>
  );
}

function ColonistCard({ pawn }: { pawn: PawnLine }) {
  return (
    <article className={`colonist-card ${pawn.isDowned ? 'warn' : ''}`}>
      <div>
        <h4>{pawn.name}</h4>
        <small>{pawn.currentJob ?? 'no current job'}</small>
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

function formatDate(snapshot: ColonySnapshot): string {
  const date = snapshot.date;
  if (date.year != null && date.quadrum && date.day != null) {
    return `${date.quadrum} ${date.day}, Y${date.year}`;
  }
  return date.raw;
}
