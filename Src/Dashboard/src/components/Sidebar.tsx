import type { ReactNode } from 'react';
import type { ColonySnapshot } from '../types/colony';

interface Props {
  snapshot: ColonySnapshot | null;
}

export function Sidebar({ snapshot }: Props) {
  if (!snapshot) {
    return (
      <aside className="side-console panel-box">
        <div className="module-empty">
          Loading colony data...
        </div>
      </aside>
    );
  }

  const date = formatDate(snapshot);
  const season = snapshot.season;
  const wealth = snapshot.wealth;
  const food = snapshot.food;
  const power = snapshot.power;
  const mood = snapshot.mood;
  const threat = snapshot.threat;
  const weather = snapshot.weather;
  const research = snapshot.research;
  const cls = snapshot.colonists;

  return (
    <aside className="side-console panel-box">
      <div className="module-header">
        <h2>Colony</h2>
        <span>Telemetry</span>
      </div>

      <Group label="Date" icon="📅">
        <Row icon="🕒" k="When" v={date} />
        <Row icon="🍂" k="Season" v={season.currentSeason ?? '-'} />
        <Row icon="❄️" k="Winter" v={season.daysToWinter != null ? `in ${season.daysToWinter}d` : '-'} />
      </Group>

      <Group label="People" icon="👥">
        <Row icon="👤" k="Total" v={cls.count.toString()} />
        <Row icon="🧑" k="Adults" v={cls.adults.toString()} />
        <Row icon="🧒" k="Children" v={cls.children.toString()} />
        {snapshot.medical.downed > 0 && <Row icon="⛑️" k="Downed" v={snapshot.medical.downed.toString()} warn />}
        {snapshot.medical.sick > 0 && <Row icon="🤒" k="Sick" v={snapshot.medical.sick.toString()} warn />}
      </Group>

      <Group label="Mood" icon="🙂">
        <Row icon="📈" k="Average" v={`${Math.round(mood.averageMood * 100)}%`} />
        {mood.breakRiskCount > 0 && <Row icon="⚠️" k="Break risk" v={mood.breakRiskCount.toString()} warn />}
      </Group>

      <Group label="Food" icon="🍲">
        <Row
          icon="⏳"
          k="Days left"
          v={food.estimatedDaysOfFood != null ? `${food.estimatedDaysOfFood.toFixed(1)}d` : '-'}
          warn={food.estimatedDaysOfFood != null && food.estimatedDaysOfFood < 7}
        />
        <Row icon="📦" k="In stockpile" v={food.estimatedFoodUnitsInStockpile.toLocaleString()} />
        <Row icon="🌾" k="Crops" v={food.totalCrops.toString()} />
      </Group>

      <Group label="Wealth" icon="💰">
        <Row icon="🏛️" k="Colony" v={wealth.colony.toLocaleString(undefined, { maximumFractionDigits: 0 })} />
        <Row icon="🧮" k="Per colonist" v={wealth.wealthPerColonist.toLocaleString(undefined, { maximumFractionDigits: 0 })} />
      </Group>

      <Group label="Power" icon="⚡">
        <Row
          icon="🔌"
          k="Net"
          v={`${power.netW >= 0 ? '+' : ''}${Math.round(power.netW)} W`}
          warn={power.netW < 0}
        />
        <Row icon="🔋" k="Production" v={`${Math.round(power.productionW)} W`} />
        <Row icon="💡" k="Consumption" v={`${Math.round(power.consumptionW)} W`} />
      </Group>

      <Group label="Threat" icon="🛡️">
        <Row
          icon="⚔️"
          k="Active raid"
          v={threat.activeRaid ? 'YES' : 'no'}
          warn={threat.activeRaid}
        />
        {threat.hostileLordCount > 0 && (
          <Row icon="🚩" k="Hostile groups" v={threat.hostileLordCount.toString()} warn />
        )}
        {threat.totalThreatPoints > 0 && (
          <Row icon="🎯" k="Threat points" v={Math.round(threat.totalThreatPoints).toString()} />
        )}
      </Group>

      <Group label="Weather" icon="🌦️">
        <Row icon="🌡️" k="Temp" v={`${weather.temperatureC.toFixed(1)} C`} />
        <Row icon="☁️" k="Type" v={weather.def ?? '-'} />
      </Group>

      <Group label="Research" icon="🔬">
        <Row icon="📚" k="Project" v={research.currentProject ?? '-'} />
        <Row icon="🧪" k="Progress" v={research.progress != null ? `${Math.round(research.progress * 100)}%` : '-'} />
      </Group>

      <div className="telemetry-footer">
        briefing v{snapshot.briefingVersion} / tick {snapshot.gameTick.toLocaleString()}
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

function Group({ label, icon, children }: { label: string; icon: string; children: ReactNode }) {
  return (
    <section className="telemetry-group">
      <h3><Emoji icon={icon} />{label}</h3>
      <div className="telemetry-rows">{children}</div>
    </section>
  );
}

function Row({ icon, k, v, warn }: { icon: string; k: string; v: string; warn?: boolean }) {
  return (
    <div className={`telemetry-row ${warn ? 'warn' : ''}`}>
      <span><Emoji icon={icon} />{k}</span>
      <strong>{v}</strong>
    </div>
  );
}

function Emoji({ icon }: { icon: string }) {
  return <span className="telemetry-icon" aria-hidden="true">{icon}</span>;
}
