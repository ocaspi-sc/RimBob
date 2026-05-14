import type { MayorAgenda, AgendaPriority } from '../../types/agenda';
import type { AdviceItem } from '../../types/advice';
import type { ScopeConfig } from '../../dashboard/scopes';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';

export function MinisterAdviceView({
  advice,
  agenda,
  previousAgenda,
  scope,
}: {
  advice: AdviceItem[];
  agenda: MayorAgenda | null;
  previousAgenda: MayorAgenda | null;
  scope: ScopeConfig;
}) {
  if (scope.key === 'mayor') {
    return <MayorAdvice agenda={agenda} previousAgenda={previousAgenda} />;
  }

  const ministerAdvice = advice.filter(item => item.minister.toLowerCase() === scope.label.toLowerCase());

  if (scope.status !== 'live') {
    return <EmptyState code="ADVICE NOT WIRED">{scope.label} is planned and not emitting advice yet.</EmptyState>;
  }

  if (ministerAdvice.length === 0) {
    return <EmptyState code="NO ACTIVE ADVICE">{scope.label} has not emitted active advice in this session.</EmptyState>;
  }

  return (
    <div className="minister-view advice-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2>Advice</h2>
        <p>Active feeder minister advice from the SSE feed. No write/control affordances.</p>
      </header>
      <div className="advice-stack">
        {ministerAdvice.map(item => <AdviceCard key={item.id} item={item} />)}
      </div>
    </div>
  );
}

function MayorAdvice({
  agenda,
  previousAgenda,
}: {
  agenda: MayorAgenda | null;
  previousAgenda: MayorAgenda | null;
}) {
  if (!agenda) {
    return <EmptyState code="NO AGENDA">Waiting for the Mayor's first agenda update.</EmptyState>;
  }

  const previousShort = new Map((previousAgenda?.short_term ?? []).map(item => [item.id, item]));
  const activeShort = agenda.short_term.filter(item => item.status === 'active');
  const closedShort = agenda.short_term.filter(item => item.status !== 'active');

  return (
    <div className="minister-view advice-view mayor-advice">
      <header className="agenda-hero">
        <div>
          <span className="eyebrow">Mayor Advice</span>
          <h2>{agenda.posture.summary}</h2>
        </div>
        <div className="agenda-stamps">
          <span>v{agenda.version}</span>
          <span>{agenda.updated_in_game_tick}</span>
          <span>{formatTime(agenda.generated_at)}</span>
        </div>
      </header>

      <section className="posture-band">
        <span>{agenda.posture.economic}</span>
        <span>{agenda.posture.military}</span>
      </section>

      <DisclosureSection title="State of the Union" defaultOpen meta={`${Object.keys(agenda.state_of_the_union).length} categories`}>
        <div className="union-grid">
          {Object.entries(agenda.state_of_the_union).map(([key, value]) => (
            <article key={key}>
              <strong>{key}</strong>
              <p>{value}</p>
            </article>
          ))}
        </div>
      </DisclosureSection>

      <DisclosureSection title="What changed" defaultOpen>
        <p className="notes-copy">{agenda.update_notes}</p>
      </DisclosureSection>

      <section className="priority-stack">
        <div className="section-heading">
          <span className="eyebrow">Short term</span>
          <h2>{activeShort.length} active priorities</h2>
        </div>
        {activeShort.map((item, index) => (
          <PriorityCard
            key={item.id}
            item={item}
            rank={index + 1}
            delta={deltaFor(item, previousShort.get(item.id))}
          />
        ))}
        {closedShort.length > 0 && (
          <DisclosureSection title="Closed this turn" meta={`${closedShort.length} items`}>
            {closedShort.map(item => (
              <PriorityCard
                key={item.id}
                item={item}
                delta={deltaFor(item, previousShort.get(item.id))}
              />
            ))}
          </DisclosureSection>
        )}
      </section>

      <DisclosureSection title="Long-term goals" defaultOpen meta={`${agenda.long_term.length} items`}>
        <div className="long-list">
          {agenda.long_term.map(item => (
            <div className={`long-row ${item.status}`} key={item.id}>
              <span>{item.status}</span>
              <p>{item.text}</p>
            </div>
          ))}
        </div>
      </DisclosureSection>

      {Object.keys(agenda.cabinet_direction).length > 0 && (
        <DisclosureSection title="Cabinet direction" meta={`${Object.keys(agenda.cabinet_direction).length} ministers`}>
          <div className="direction-grid">
            {Object.entries(agenda.cabinet_direction).map(([minister, direction]) => (
              <article key={minister}>
                <strong>{minister}</strong>
                <p>{direction}</p>
              </article>
            ))}
          </div>
        </DisclosureSection>
      )}
    </div>
  );
}

function AdviceCard({ item }: { item: AdviceItem }) {
  return (
    <article className={`advice-card v2 ${item.priority}`}>
      <header>
        <div>
          <span className="eyebrow">{item.advice_type}</span>
          <h3>{item.title}</h3>
        </div>
        <div className="advice-badges">
          <span>{item.priority}</span>
        </div>
      </header>
      <p>{item.body}</p>
      <blockquote>{item.rationale}</blockquote>
      {item.resource_requests.length > 0 && (
        <DisclosureSection title="Resource requests" defaultOpen meta={`${item.resource_requests.length} requests`}>
          <div className="dense-table resource-table">
            <div className="dense-row header">
              <span>Kind</span>
              <span>Request</span>
              <span>Reason</span>
              <span>Qty</span>
              <span>Owner</span>
              <span>Work / Skill</span>
              <span>Priority</span>
            </div>
            {item.resource_requests.map((request, index) => (
              <div className="dense-row" key={`${item.id}-request-${index}`}>
                <span>{formatLabel(request.kind)}</span>
                <span>{request.request}</span>
                <span>{request.reason}</span>
                <span>{formatQuantity(request.quantity)}</span>
                <span>{request.requested_from ?? '-'}</span>
                <span>{formatWorkSkill(request.work_type, request.skill)}</span>
                <span>{formatLabel(request.priority)}</span>
              </div>
            ))}
          </div>
        </DisclosureSection>
      )}
      {item.suggested_actions.length > 0 && (
        <DisclosureSection title="Suggested actions" meta={`${item.suggested_actions.length} actions`}>
          <div className="action-list">
            {item.suggested_actions.map((action, index) => (
              <div key={`${item.id}-action-${index}`}>
                <strong>{action.kind}</strong>
                <span>{action.instruction}</span>
              </div>
            ))}
          </div>
        </DisclosureSection>
      )}
    </article>
  );
}

function PriorityCard({
  delta,
  item,
  rank,
}: {
  delta: string | null;
  item: AgendaPriority;
  rank?: number;
}) {
  return (
    <article className={`priority-card ${item.status}`}>
      {rank && <span className="rank">{rank}</span>}
      <p>{item.text}</p>
      <footer>
        <span>{item.status}</span>
        {delta && <strong>{delta}</strong>}
      </footer>
    </article>
  );
}

function deltaFor(current: AgendaPriority, previous: AgendaPriority | undefined): string | null {
  if (!previous) return 'new';
  if (current.status !== previous.status) return current.status;
  if (current.text !== previous.text) return 'updated';
  return null;
}

function formatTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function formatLabel(value: string | null | undefined): string {
  if (!value) return '-';
  return value
    .replace(/_/g, ' ')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function formatQuantity(value: number | null | undefined): string {
  return value === null || value === undefined ? '-' : value.toLocaleString();
}

function formatWorkSkill(workType: string | null | undefined, skill: string | null | undefined): string {
  const parts = [workType, skill]
    .filter((value): value is string => Boolean(value))
    .map(formatLabel);
  return parts.length > 0 ? parts.join(' / ') : '-';
}
