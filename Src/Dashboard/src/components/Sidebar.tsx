import type { CSSProperties, ReactNode } from 'react';
import type { ColonySnapshot } from '../types/colony';

interface Props {
  snapshot: ColonySnapshot | null;
}

export function Sidebar({ snapshot }: Props) {
  if (!snapshot) {
    return (
      <aside style={sidebarStyle}>
        <div style={{ color: '#9ca3af', fontSize: '0.85rem', textAlign: 'center', padding: '1rem 0' }}>
          Loading colony data…
        </div>
      </aside>
    );
  }

  const date     = formatDate(snapshot);
  const season   = snapshot.season;
  const wealth   = snapshot.wealth;
  const food     = snapshot.food;
  const power    = snapshot.power;
  const mood     = snapshot.mood;
  const threat   = snapshot.threat;
  const weather  = snapshot.weather;
  const research = snapshot.research;
  const cls      = snapshot.colonists;

  return (
    <aside style={sidebarStyle}>
      <h2 style={asideTitle}>Colony</h2>

      <Group label="Date">
        <Row k="When"   v={date} />
        <Row k="Season" v={season.currentSeason ?? '—'} />
        <Row k="Winter" v={season.daysToWinter != null ? `in ${season.daysToWinter}d` : '—'} />
      </Group>

      <Group label="People">
        <Row k="Total"    v={cls.count.toString()} />
        <Row k="Adults"   v={cls.adults.toString()} />
        <Row k="Children" v={cls.children.toString()} />
        {snapshot.medical.downed > 0 && <Row k="Downed" v={snapshot.medical.downed.toString()} warn />}
        {snapshot.medical.sick   > 0 && <Row k="Sick"   v={snapshot.medical.sick.toString()}   warn />}
      </Group>

      <Group label="Mood">
        <Row k="Average" v={`${Math.round(mood.averageMood * 100)}%`} />
        {mood.breakRiskCount > 0 && <Row k="Break risk" v={mood.breakRiskCount.toString()} warn />}
      </Group>

      <Group label="Food">
        <Row
          k="Days left"
          v={food.estimatedDaysOfFood != null ? `${food.estimatedDaysOfFood.toFixed(1)}d` : '—'}
          warn={food.estimatedDaysOfFood != null && food.estimatedDaysOfFood < 7}
        />
        <Row k="In stockpile" v={food.estimatedFoodUnitsInStockpile.toLocaleString()} />
        <Row k="Crops" v={food.totalCrops.toString()} />
      </Group>

      <Group label="Wealth">
        <Row k="Colony"       v={wealth.colony.toLocaleString(undefined, { maximumFractionDigits: 0 })} />
        <Row k="Per colonist" v={wealth.wealthPerColonist.toLocaleString(undefined, { maximumFractionDigits: 0 })} />
      </Group>

      <Group label="Power">
        <Row
          k="Net"
          v={`${power.netW >= 0 ? '+' : ''}${Math.round(power.netW)} W`}
          warn={power.netW < 0}
        />
        <Row k="Production"  v={`${Math.round(power.productionW)} W`} />
        <Row k="Consumption" v={`${Math.round(power.consumptionW)} W`} />
      </Group>

      <Group label="Threat">
        <Row
          k="Active raid"
          v={threat.activeRaid ? 'YES' : 'no'}
          warn={threat.activeRaid}
        />
        {threat.hostileLordCount > 0 && (
          <Row k="Hostile groups" v={threat.hostileLordCount.toString()} warn />
        )}
        {threat.totalThreatPoints > 0 && (
          <Row k="Threat points" v={Math.round(threat.totalThreatPoints).toString()} />
        )}
      </Group>

      <Group label="Weather">
        <Row k="Temp" v={`${weather.temperatureC.toFixed(1)} °C`} />
        <Row k="Type" v={weather.def ?? '—'} />
      </Group>

      <Group label="Research">
        <Row k="Project"  v={research.currentProject ?? '—'} />
        <Row k="Progress" v={research.progress != null ? `${Math.round(research.progress * 100)}%` : '—'} />
      </Group>

      <div style={{ fontSize: '0.7rem', color: '#9ca3af', marginTop: '0.5rem', textAlign: 'right' }}>
        briefing v{snapshot.briefingVersion} · tick {snapshot.gameTick.toLocaleString()}
      </div>
    </aside>
  );
}

function formatDate(s: ColonySnapshot): string {
  const d = s.date;
  if (d.year != null && d.quadrum && d.day != null) {
    return `${d.day}${ordinal(d.day)} ${d.quadrum}, ${d.year}`;
  }
  return d.raw;
}

function ordinal(n: number): string {
  const r10 = n % 10, r100 = n % 100;
  if (r10 === 1 && r100 !== 11) return 'st';
  if (r10 === 2 && r100 !== 12) return 'nd';
  if (r10 === 3 && r100 !== 13) return 'rd';
  return 'th';
}

function Group({ label, children }: { label: string; children: ReactNode }) {
  return (
    <section style={{ marginBottom: '0.85rem' }}>
      <h3 style={groupHead}>{label}</h3>
      <div>{children}</div>
    </section>
  );
}

function Row({ k, v, warn }: { k: string; v: string; warn?: boolean }) {
  return (
    <div style={{
      display: 'flex',
      justifyContent: 'space-between',
      gap: '0.5rem',
      padding: '0.18rem 0',
      fontSize: '0.83rem',
      borderBottom: '1px solid #f3f4f6',
    }}>
      <span style={{ color: '#6b7280' }}>{k}</span>
      <span style={{
        color: warn ? '#b91c1c' : '#1f2937',
        fontWeight: warn ? 600 : 400,
        fontVariantNumeric: 'tabular-nums',
        textAlign: 'right',
      }}>
        {v}
      </span>
    </div>
  );
}

const sidebarStyle: CSSProperties = {
  width: 240,
  flexShrink: 0,
  padding: '1rem 1.1rem',
  background: '#fafafa',
  border: '1px solid #e5e7eb',
  borderRadius: 10,
  alignSelf: 'flex-start',
  position: 'sticky',
  top: '1rem',
  fontFamily: 'system-ui, sans-serif',
};

const asideTitle: CSSProperties = {
  margin: '0 0 0.75rem',
  fontSize: '0.78rem',
  textTransform: 'uppercase',
  letterSpacing: '0.08em',
  color: '#374151',
  borderBottom: '1px solid #e5e7eb',
  paddingBottom: '0.5rem',
};

const groupHead: CSSProperties = {
  fontSize: '0.72rem',
  textTransform: 'uppercase',
  letterSpacing: '0.06em',
  color: '#9ca3af',
  margin: '0 0 0.25rem',
  fontWeight: 600,
};
