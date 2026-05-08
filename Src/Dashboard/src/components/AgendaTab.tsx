import type { ReactNode } from 'react';
import type { MayorAgenda, AgendaItem } from '../types/agenda';
import { AgendaItemCard, type DeltaBadge } from './AgendaItemCard';

interface Props {
  agenda: MayorAgenda | null;
  previous: MayorAgenda | null;
}

export function AgendaTab({ agenda, previous }: Props) {
  if (!agenda) {
    return (
      <div className="empty-console">
        <span className="empty-code">MAYOR UPLINK</span>
        Waiting for the first briefing... Mayor is calling Gemini.
      </div>
    );
  }

  const previousShort = new Map<string, AgendaItem>(
    (previous?.short_term ?? []).map(i => [i.id, i] as const),
  );

  const ministerEntries = Object.entries(agenda.minister_direction ?? {});
  const activeShort = agenda.short_term.filter(i => i.status === 'active');
  const closedShort = agenda.short_term.filter(i => i.status !== 'active');

  return (
    <div className="agenda-console">
      <AgendaHeader agenda={agenda} />

      <Section title="State of the Union" tone="violet">
        <StateOfTheUnion entries={agenda.state_of_the_union} />
      </Section>

      <Section title="What changed" tone="amber">
        <p className="update-notes">
          {agenda.update_notes}
        </p>
      </Section>

      <Section
        title="Short-term priorities"
        tone="green"
        count={activeShort.length}
        countLabel="active"
      >
        {agenda.short_term.length === 0 && <Empty>No short-term priorities.</Empty>}
        {activeShort.map((item, idx) => (
          <AgendaItemCard
            key={item.id}
            item={item}
            rank={idx + 1}
            delta={computeDelta(item, previousShort.get(item.id))}
          />
        ))}
        {closedShort.length > 0 && (
          <details className="closed-priorities">
            <summary>
              {closedShort.length} closed this turn
            </summary>
            <div>
              {closedShort.map(item => (
                <AgendaItemCard
                  key={item.id}
                  item={item}
                  delta={computeDelta(item, previousShort.get(item.id))}
                />
              ))}
            </div>
          </details>
        )}
      </Section>

      <Section
        title="Long-term goals"
        tone="cyan"
        count={agenda.long_term.length}
      >
        {agenda.long_term.length === 0 && <Empty>No long-term goals.</Empty>}
        {agenda.long_term.length > 0 && (
          <div className="boxed-list">
            {agenda.long_term.map((item, i) => (
              <LongTermRow key={item.id} item={item} divider={i > 0} />
            ))}
          </div>
        )}
      </Section>

      {ministerEntries.length > 0 && (
        <Section title="Minister direction" tone="magenta" count={ministerEntries.length}>
          <div className="minister-grid">
            {ministerEntries.map(([minister, direction]) => (
              <div
                key={minister}
                className="minister-card"
              >
                <div>
                  {minister}
                </div>
                <p>{direction}</p>
              </div>
            ))}
          </div>
        </Section>
      )}
    </div>
  );
}

function AgendaHeader({ agenda }: { agenda: MayorAgenda }) {
  return (
    <header className="agenda-header">
      <div className="agenda-header-top">
        <div className="posture-row">
          <PostureBadge label={agenda.posture.economic} kind="economic" />
          <PostureBadge label={agenda.posture.military} kind="military" />
        </div>
        <div className="agenda-version">
          v{agenda.version} / tick {agenda.updated_in_game_tick}
        </div>
      </div>
      <div className="posture-summary">
        {agenda.posture.summary}
      </div>
    </header>
  );
}

function PostureBadge({ label, kind }: { label: string; kind: 'economic' | 'military' }) {
  return (
    <span className={`posture-badge ${kind}`}>
      <span aria-hidden>{kind === 'economic' ? 'ECO' : 'MIL'}</span>
      {label}
    </span>
  );
}

const ministerMeta: Record<string, { code: string; label: string; order: number }> = {
  agriculture: { code: 'AGR', label: 'Agriculture', order: 1 },
  defense: { code: 'DEF', label: 'Defense', order: 2 },
  welfare: { code: 'WEL', label: 'Welfare', order: 3 },
  construction: { code: 'CON', label: 'Construction', order: 4 },
  treasury: { code: 'TRE', label: 'Treasury', order: 5 },
  research: { code: 'RSH', label: 'Research', order: 6 },
};

function StateOfTheUnion({ entries }: { entries: Record<string, string> }) {
  const keys = Object.keys(entries);
  if (keys.length === 0) {
    return <Empty>No state-of-the-union entries this turn.</Empty>;
  }

  // Order known categories first, then anything unexpected at the end.
  const sorted = keys.slice().sort((a, b) => {
    const oa = ministerMeta[a]?.order ?? 99;
    const ob = ministerMeta[b]?.order ?? 99;
    return oa - ob;
  });

  return (
    <ul className="union-list">
      {sorted.map(key => {
        const meta = ministerMeta[key] ?? { code: 'SYS', label: capitalise(key), order: 99 };
        return (
          <li key={key}>
            <span aria-hidden>{meta.code}</span>
            <strong>
              {meta.label}
            </strong>
            <p>{entries[key]}</p>
          </li>
        );
      })}
    </ul>
  );
}

function capitalise(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}

function computeDelta(current: AgendaItem, prev: AgendaItem | undefined): DeltaBadge {
  if (!prev) return 'new';
  if (current.status === 'completed' && prev.status !== 'completed') return 'done';
  if (current.status === 'deferred' && prev.status !== 'deferred') return 'deferred';
  if (current.text !== prev.text) return 'updated';
  return null;
}

interface SectionProps {
  title: string;
  tone: 'violet' | 'amber' | 'green' | 'cyan' | 'magenta';
  children: ReactNode;
  count?: number;
  countLabel?: string;
}

function Section({ title, tone, children, count, countLabel }: SectionProps) {
  return (
    <section className={`dashboard-section tone-${tone}`}>
      <h3>
        <span>{title}</span>
        {count !== undefined && (
          <span>
            {count}{countLabel ? ` ${countLabel}` : ''}
          </span>
        )}
      </h3>
      {children}
    </section>
  );
}

function Empty({ children }: { children: ReactNode }) {
  return <div className="module-empty">{children}</div>;
}

function LongTermRow({ item, divider }: { item: AgendaItem; divider: boolean }) {
  return (
    <div className={`long-term-row ${divider ? 'divided' : ''} ${item.status}`}>
      <span aria-hidden />
      <p>
        {item.text}
      </p>
      <strong>
        {item.status}
      </strong>
    </div>
  );
}
