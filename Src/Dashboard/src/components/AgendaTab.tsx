import { useEffect, useId, useState, type ReactNode } from 'react';
import type { MayorAgenda, AgendaPriority } from '../types/agenda';
import type { RimAIStatus } from '../types/status';
import { fetchMayorPrompt } from '../api/status';
import { AgendaPriorityCard, type DeltaBadge } from './AgendaPriorityCard';
import {
  DecoratedPlainText,
  ReadableJsonView,
  isJsonRecord,
  keywordIconForText,
  summariseJsonValue,
  type JsonRecord,
  type JsonValue,
} from './PromptReadableView';

interface Props {
  agenda: MayorAgenda | null;
  previous: MayorAgenda | null;
  status: RimAIStatus | null;
}

export function AgendaTab({ agenda, previous, status }: Props) {
  const [showPrompt, setShowPrompt] = useState(false);

  if (!agenda) {
    return <MayorUplinkState status={status} />;
  }

  const previousShort = new Map<string, AgendaPriority>(
    (previous?.short_term ?? []).map(i => [i.id, i] as const),
  );

  const ministerEntries = Object.entries(agenda.cabinet_direction ?? {});
  const activeShort = agenda.short_term.filter(i => i.status === 'active');
  const closedShort = agenda.short_term.filter(i => i.status !== 'active');

  if (showPrompt) {
    return (
      <div className="prompt-page">
        <AgendaHeader agenda={agenda} showPrompt={showPrompt} onTogglePrompt={() => setShowPrompt(p => !p)} />
        <PromptFull />
      </div>
    );
  }

  return (
    <div className="agenda-console">
      <AgendaHeader agenda={agenda} showPrompt={showPrompt} onTogglePrompt={() => setShowPrompt(p => !p)} />

      <>

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
          <AgendaPriorityCard
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
                <AgendaPriorityCard
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

      </>
    </div>
  );
}

// ── Waiting state ─────────────────────────────────────────────────────────────

function formatElapsed(seconds: number): string {
  const m = Math.floor(seconds / 60).toString().padStart(2, '0');
  const s = (seconds % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

function MayorUplinkState({ status }: { status: RimAIStatus | null }) {
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    if (!status?.mayor_running || !status.mayor_started_at) return;
    const start = new Date(status.mayor_started_at).getTime();
    const tick = () => setElapsed(Math.floor((Date.now() - start) / 1000));
    tick();
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [status?.mayor_running, status?.mayor_started_at]);

  if (!status) {
    return (
      <div className="mayor-uplink">
        <span className="empty-code">MAYOR UPLINK</span>
        <p>Connecting to server…</p>
      </div>
    );
  }

  if (!status.llm_configured) {
    return (
      <div className="mayor-uplink error">
        <span className="empty-code">MAYOR OFFLINE</span>
        <p>
          Gemini not configured —{' '}
          set <code>GEMINI_API_KEY</code> and restart the server.
        </p>
        <PromptViewer />
      </div>
    );
  }

  return (
    <div className="mayor-uplink">
      <span className="empty-code">MAYOR UPLINK</span>
      {status.mayor_running ? (
        <p>
          Calling Gemini…{' '}
          <span className="uplink-timer">{formatElapsed(elapsed)}</span>
        </p>
      ) : (
        <p>Waiting for Mayor cycle…</p>
      )}
      {status.mayor_last_error && (
        <p className="uplink-error">Last error: {status.mayor_last_error}</p>
      )}
      <PromptViewer />
    </div>
  );
}

export function PromptFull() {
  const [prompt, setPrompt] = useState<{ system: string; user: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    fetchMayorPrompt()
      .then(setPrompt)
      .catch(err => setError(String(err)))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="prompt-full-status">Loading prompt…</div>;
  if (error)   return <div className="prompt-full-status error">{error}</div>;
  if (!prompt) return null;

  return (
    <div className="prompt-full">
      <div className="prompt-section">
        <h4>Briefing (user message)</h4>
        <BriefingPrompt raw={prompt.user} />
      </div>
      <div className="prompt-section prompt-section-compact">
        <PromptTextBox title="System prompt" text={prompt.system} />
      </div>
    </div>
  );
}

function PromptViewer() {
  const [prompt, setPrompt] = useState<{ system: string; user: string } | null>(null);
  const [loading, setLoading] = useState(false);

  const handleToggle = (e: React.SyntheticEvent<HTMLDetailsElement>) => {
    if (!(e.target as HTMLDetailsElement).open || prompt || loading) return;
    setLoading(true);
    fetchMayorPrompt()
      .then(setPrompt)
      .catch(err => console.error('fetchMayorPrompt', err))
      .finally(() => setLoading(false));
  };

  return (
    <details className="prompt-viewer" onToggle={handleToggle}>
      <summary>What was sent to Gemini</summary>
      {loading && <p className="prompt-loading">Loading…</p>}
      {prompt && (
        <>
          <h4>Briefing (user message)</h4>
          <BriefingPrompt raw={prompt.user} />
          <PromptTextBox title="System prompt" text={prompt.system} />
        </>
      )}
    </details>
  );
}

interface BriefingGroup {
  title: string;
  keys: string[];
}

const briefingGroups: BriefingGroup[] = [
  { title: 'Overview', keys: ['briefingVersion', 'date', 'gameTick', 'season'] },
  { title: 'People', keys: ['colonists', 'skills', 'traits', 'medical', 'prisoners'] },
  { title: 'Food & resources', keys: ['food', 'resources'] },
  { title: 'Infrastructure', keys: ['power', 'buildings'] },
  { title: 'Welfare & threat', keys: ['mood', 'threat', 'wealth'] },
  { title: 'Environment', keys: ['weather'] },
  { title: 'Research', keys: ['research'] },
];

function BriefingPrompt({ raw }: { raw: string }) {
  const parsed = parseJson(raw);
  if (!isJsonRecord(parsed)) {
    return <DecoratedPlainText text={raw} />;
  }

  const briefingFromPayload = parsed.briefing;
  const hasBriefingPayload = isJsonRecord(briefingFromPayload);
  const briefing = hasBriefingPayload ? briefingFromPayload : parsed;
  const usedKeys = new Set(briefingGroups.flatMap(group => group.keys.map(normaliseKey)));
  const extraKeys = Object.keys(briefing).filter(key => !usedKeys.has(normaliseKey(key)));
  const groups = extraKeys.length === 0
    ? briefingGroups
    : [...briefingGroups, { title: 'Other briefing fields', keys: extraKeys }];
  const context = hasBriefingPayload ? withoutKey(parsed, 'briefing') : {};

  return (
    <div className="briefing-parts">
      {groups.map(group => {
        const section = pickKeys(briefing, group.keys);
        if (Object.keys(section).length === 0) return null;
        return (
          <CollapsibleBox
            key={group.title}
            icon={keywordIconForText(group.title)}
            title={group.title}
            meta={summariseJsonValue(section)}
          >
            <ReadableJsonView value={section} />
          </CollapsibleBox>
        );
      })}
      {Object.keys(context).length > 0 && (
        <CollapsibleBox
          icon={keywordIconForText('context guides directives agenda')}
          title="Prompt context"
          meta={summariseJsonValue(context)}
        >
          <ReadableJsonView value={context} />
        </CollapsibleBox>
      )}
    </div>
  );
}

function PromptTextBox({ title, text }: { title: string; text: string }) {
  return (
    <CollapsibleBox
      className="prompt-text-box"
      icon={keywordIconForText(title)}
      title={title}
      meta={`${text.length.toLocaleString()} chars`}
    >
      <DecoratedPlainText text={text} />
    </CollapsibleBox>
  );
}

function CollapsibleBox({
  children,
  className,
  icon,
  meta,
  title,
}: {
  children: ReactNode;
  className?: string;
  icon?: string | null;
  meta: string;
  title: string;
}) {
  const [isOpen, setIsOpen] = useState(false);
  const buttonId = useId();
  const panelId = useId();

  return (
    <section className={`briefing-box${isOpen ? ' open' : ''}${className ? ` ${className}` : ''}`}>
      <h5 className="briefing-box-heading">
        <button
          id={buttonId}
          type="button"
          className="briefing-box-trigger"
          aria-expanded={isOpen}
          aria-controls={panelId}
          onClick={() => setIsOpen(open => !open)}
        >
          <span className="briefing-box-title">
            {icon && <span className="briefing-box-icon" aria-hidden>{icon}</span>}
            {title}
          </span>
          <small className="briefing-box-meta">{meta}</small>
        </button>
      </h5>
      {isOpen && (
        <div
          id={panelId}
          className="briefing-box-panel"
          role="region"
          aria-labelledby={buttonId}
        >
          {children}
        </div>
      )}
    </section>
  );
}

function parseJson(raw: string): JsonValue | undefined {
  try { return JSON.parse(raw) as JsonValue; }
  catch { return undefined; }
}

function pickKeys(source: JsonRecord, keys: string[]): JsonRecord {
  const wanted = new Set(keys.map(normaliseKey));
  return Object.entries(source).reduce<JsonRecord>((acc, [key, value]) => {
    if (wanted.has(normaliseKey(key))) acc[key] = value;
    return acc;
  }, {});
}

function normaliseKey(key: string): string {
  return key.toLowerCase();
}

function withoutKey(source: JsonRecord, keyToOmit: string): JsonRecord {
  return Object.entries(source).reduce<JsonRecord>((acc, [key, value]) => {
    if (key !== keyToOmit) acc[key] = value;
    return acc;
  }, {});
}

// ── Header ────────────────────────────────────────────────────────────────────

function formatTimestamp(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function AgendaHeader({ agenda, showPrompt, onTogglePrompt }: {
  agenda: MayorAgenda;
  showPrompt: boolean;
  onTogglePrompt: () => void;
}) {
  return (
    <header className="agenda-header">
      <div className="agenda-header-left">
        <div className="posture-row">
          <PostureBadge label={agenda.posture.economic} kind="economic" />
          <PostureBadge label={agenda.posture.military} kind="military" />
        </div>
        <div className="posture-summary">
          {agenda.posture.summary}
        </div>
      </div>
      <div className="agenda-meta">
        <span className="agenda-version">v{agenda.version} · {agenda.updated_in_game_tick}</span>
        <span className="agenda-timestamp" title={agenda.generated_at}>
          Generated {formatTimestamp(agenda.generated_at)}
        </span>
        <button
          className={`prompt-toggle-btn ${showPrompt ? 'active' : ''}`}
          onClick={onTogglePrompt}
          title="Toggle Gemini prompt view"
        >
          {showPrompt ? 'Agenda' : 'Prompt'}
        </button>
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

function computeDelta(current: AgendaPriority, prev: AgendaPriority | undefined): DeltaBadge {
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

function LongTermRow({ item, divider }: { item: AgendaPriority; divider: boolean }) {
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
