import { DisclosureSection } from '../shared/DisclosureSection';
import { iconForField, iconForInfoTerm, iconForScope } from '../../dashboard/semanticIcons';
import { SemanticLabel } from '../shared/SemanticIcon';

// ── Highlights data ──────────────────────────────────────────────────────────

interface FeatureEntry {
  name: string;
  tag: string;
  description: string;
}

const features: FeatureEntry[] = [
  {
    name: 'Minister pattern',
    tag: 'Architecture',
    description: 'Colony decisions are split across independent advisors — Food and Mayor — each with its own briefing, rules, and LLM escalation path.',
  },
  {
    name: 'Rules-first path',
    tag: 'Runtime',
    description: 'Deterministic rules run on every tick. The LLM is only called when rule logic hits a judgment call, keeping latency and cost low.',
  },
  {
    name: 'LLM escalation',
    tag: 'Runtime',
    description: 'Gemini is consulted only when rules need interpretation or tradeoff reasoning. RAG snippets from the local corpus accompany every prompt.',
  },
  {
    name: 'Versioned aggregates',
    tag: 'State',
    description: '13+ typed state slices (food, economy, threats, power, research…) are version-stamped so ministers skip recompute when nothing changed.',
  },
  {
    name: 'Advice bus',
    tag: 'Coordination',
    description: 'Ministers publish to a shared pub/sub bus. They never call each other directly; cross-cutting signals travel as typed Flags.',
  },
  {
    name: 'RAG knowledge base',
    tag: 'Knowledge',
    description: 'A local RimWorld guide corpus is chunked, embedded, and retrieved per-query. Ministers cite sources; humans can verify the reasoning.',
  },
  {
    name: 'SSE streaming',
    tag: 'Live feed',
    description: 'The dashboard receives agenda and advice updates in real time via /api/advice/stream — no polling, no stale snapshots.',
  },
  {
    name: 'Replay corpus',
    tag: 'Refine',
    description: 'Every minister input and output is appended to an immutable log. Rule and prompt changes can be back-tested against real colony history.',
  },
];

interface StatEntry {
  label: string;
  value: string;
}

const codebaseStats: StatEntry[] = [
  { label: 'Total LOC', value: '~25,200' },
  { label: 'C# files', value: '154' },
  { label: 'C# LOC', value: '~19,700' },
  { label: 'TypeScript / TSX files', value: '55' },
  { label: 'TypeScript LOC', value: '~5,500' },
  { label: 'C# types (classes / records / interfaces / enums)', value: '~303' },
  { label: 'Test files', value: '40' },
  { label: 'Test LOC', value: '~6,900' },
];

const projectStats: StatEntry[] = [
  { label: 'Backend projects', value: '9' },
  { label: 'State aggregates', value: '13+' },
  { label: 'API endpoints', value: '~14' },
  { label: 'Ministers', value: '2 (Food, Mayor)' },
  { label: 'React components', value: '~40' },
  { label: 'Custom hooks', value: '6' },
  { label: 'Disk size', value: '~1.2 GB (incl. build artifacts)' },
  { label: 'Est. build time (solo senior SWE)', value: '6–9 months' },
];

interface DecisionEntry {
  decision: string;
  tag: string;
  rationale: string;
}

const designDecisions: DecisionEntry[] = [
  {
    decision: 'Ministers are decoupled',
    tag: 'Coupling',
    rationale: 'Direct minister-to-minister calls create ordering and recursion problems. Flags on the advice bus let them communicate without knowing about each other.',
  },
  {
    decision: 'Briefings hold derived facts only',
    tag: 'Prompts',
    rationale: 'Raw game state in a prompt inflates token cost and confuses the model. Briefings pre-compute the facts that matter — food days, threat level, worker gaps.',
  },
  {
    decision: 'Rules before LLM, always',
    tag: 'Cost',
    rationale: 'Most ticks have obvious answers a rule can emit in <1 ms. Reserving the LLM for genuine ambiguity cuts API spend and makes advice predictable.',
  },
  {
    decision: 'RAG over fine-tuning',
    tag: 'Knowledge',
    rationale: 'The RimWorld corpus changes when guides are updated. Retrieval keeps knowledge current without retraining; citations let players spot outdated snippets.',
  },
  {
    decision: 'Append-only replay corpus',
    tag: 'Safety',
    rationale: 'Refinements must be testable against real history. Mutating the log would invalidate that signal, so writes are append-only and deletions are forbidden.',
  },
  {
    decision: 'Versioned snapshots, not diffs',
    tag: 'State',
    rationale: 'Diffs are hard to replay and easy to corrupt. Full versioned snapshots are slightly larger but make any point-in-time state trivially reconstructable.',
  },
];

// ── Glossary data ────────────────────────────────────────────────────────────

interface GlossaryEntry {
  term: string;
  tag: string;
  description: string;
}

const rimbobGlossary: GlossaryEntry[] = [
  {
    term: 'Agenda',
    tag: 'Mayor',
    description: 'The living plan: current posture, short-term priorities, long-term goals, and cabinet direction.',
  },
  {
    term: 'AdviceItem',
    tag: 'Cabinet',
    description: 'A feeder minister memo with priority, rationale, concrete actions, and citations.',
  },
  {
    term: 'Briefing',
    tag: 'State',
    description: 'A compact minister-specific state packet. Derived facts belong here, not as raw dumps in prompts.',
  },
  {
    term: 'Flag',
    tag: 'Coordination',
    description: 'A cross-minister signal for urgency, conflict, or a requested resource. Ministers do not talk directly.',
  },
  {
    term: 'SSE',
    tag: 'Live feed',
    description: 'Server-Sent Events from /api/advice/stream. Agenda and active advice updates reach the dashboard through it.',
  },
  {
    term: 'RAG',
    tag: 'Knowledge',
    description: 'Guide-backed retrieval that gives ministers citations and snippets from the local RimWorld corpus.',
  },
  {
    term: 'Rules path',
    tag: 'Runtime',
    description: 'The cheap deterministic route. A rule emits advice or explains why the LLM must be escalated.',
  },
  {
    term: 'LLM escalation',
    tag: 'Runtime',
    description: 'A judgment call sent to Gemini only when the rules need interpretation or tradeoff reasoning.',
  },
  {
    term: 'Replay corpus',
    tag: 'Refine',
    description: 'Append-only historical minister inputs and outputs used to test rule or prompt refinements later.',
  },
];

const rimworldSignals: GlossaryEntry[] = [
  {
    term: 'Food days',
    tag: 'Food',
    description: 'Estimated buffer before the colony runs out of food. Unknown is a telemetry gap, not safety.',
  },
  {
    term: 'Work type',
    tag: 'Labor',
    description: 'The RimWorld work-tab category a pawn performs, such as Cook, Grow, PlantCut, Haul, or Construct.',
  },
  {
    term: 'Skill',
    tag: 'Pawn',
    description: 'A pawn capability signal. It helps choose who is suitable, but it is not the same as a work request.',
  },
  {
    term: 'Downed',
    tag: 'Medical',
    description: 'A pawn cannot act. This is more urgent than low health alone and should shape Mayor/minister advice.',
  },
  {
    term: 'Wealth pressure',
    tag: 'Threat',
    description: 'Higher colony wealth increases raid danger. Advice should balance growth against defense readiness.',
  },
  {
    term: 'Power net',
    tag: 'Construction',
    description: 'Production minus consumption. Negative net power means batteries drain and cold-chain advice changes.',
  },
];

const scopeGuide = [
  {
    name: 'SYSTEM',
    tag: 'Operations',
    description: 'Raw runtime health, endpoint coverage, logs, traces, LLM provider state, and SSE diagnostics.',
  },
  {
    name: 'ANALYTICS',
    tag: 'Signals',
    description: 'Interpreted live metrics: advice mix, colony pressure, SSE health summary, and candidate analytics.',
  },
  {
    name: 'DEV BLOG',
    tag: 'Repository',
    description: 'Git-derived master history analytics: topic timeline, commit-size spikes, LOC growth, pie charts, and editorial suggestions.',
  },
  {
    name: 'Mayor / Food / Cabinet',
    tag: 'Inspection',
    description: 'Minister-specific prompt, briefing, RAG, rules, raw LLM output, and advice evidence.',
  },
];

export function InfoOverview() {
  return (
    <div className="info-overview">
      <header className="info-hero system-card reference-card">
        <div>
          <span className="eyebrow">Reference</span>
          <h2><SemanticLabel icon={iconForScope('info')} size="sm"><span>INFO</span></SemanticLabel></h2>
          <p>Plain-language vocabulary and scope guide. Live metrics belong in ANALYTICS; raw debugging belongs in SYSTEM.</p>
        </div>
        <div className="scope-boundary-strip info-boundary-strip">
          <span>Glossary</span>
          <span>Scope guide</span>
          <span>No live metrics</span>
          <span>No controls</span>
        </div>
      </header>

      {/* ── Highlights ─────────────────────────────────────────────────── */}
      <DisclosureSection title={<SemanticLabel icon={iconForScope('info')}><span>What RimBob does</span></SemanticLabel>} defaultOpen meta={`${features.length} features`}>
        <div className="analytics-ideas">
          {features.map(f => (
            <article key={f.name} className="analytics-idea">
              <div>
                <SemanticLabel icon={iconForInfoTerm(f.name, f.tag)}><strong>{f.name}</strong></SemanticLabel>
                <span>{f.tag}</span>
              </div>
              <p>{f.description}</p>
            </article>
          ))}
        </div>
      </DisclosureSection>

      <DisclosureSection title={<SemanticLabel icon={iconForField('data_coverage')}><span>By the numbers</span></SemanticLabel>} meta="codebase stats">
        <div className="glossary-columns">
          <StatList title="Code" entries={codebaseStats} />
          <StatList title="Project" entries={projectStats} />
        </div>
      </DisclosureSection>

      <DisclosureSection title={<SemanticLabel icon={iconForField('rules')}><span>Design decisions</span></SemanticLabel>} meta={`${designDecisions.length} choices`}>
        <div className="analytics-ideas">
          {designDecisions.map(d => (
            <article key={d.decision} className="analytics-idea">
              <div>
                <SemanticLabel icon={iconForInfoTerm(d.decision, d.tag)}><strong>{d.decision}</strong></SemanticLabel>
                <span>{d.tag}</span>
              </div>
              <p>{d.rationale}</p>
            </article>
          ))}
        </div>
      </DisclosureSection>

      {/* ── Glossary ────────────────────────────────────────────────────── */}
      <DisclosureSection title={<SemanticLabel icon={iconForField('info')}><span>Important buzzwords</span></SemanticLabel>} defaultOpen meta={`${rimbobGlossary.length + rimworldSignals.length} terms`}>
        <div className="glossary-columns">
          <GlossaryList title="RimBob terms" entries={rimbobGlossary} />
          <GlossaryList title="RimWorld signals" entries={rimworldSignals} />
        </div>
      </DisclosureSection>

      <DisclosureSection title={<SemanticLabel icon={iconForScope('system')}><span>Where to look</span></SemanticLabel>} defaultOpen meta="scope guide">
        <div className="analytics-ideas">
          {scopeGuide.map(scope => (
            <article key={scope.name} className="analytics-idea">
              <div>
                <SemanticLabel icon={iconForInfoTerm(scope.name, scope.tag)}><strong>{scope.name}</strong></SemanticLabel>
                <span>{scope.tag}</span>
              </div>
              <p>{scope.description}</p>
            </article>
          ))}
        </div>
      </DisclosureSection>
    </div>
  );
}

function StatList({ entries, title }: { entries: StatEntry[]; title: string }) {
  return (
    <section className="count-panel">
      <h3>{title}</h3>
      <div className="count-rows">
        {entries.map(entry => (
          <div className="count-row" key={entry.label}>
            <SemanticLabel icon={iconForInfoTerm(entry.label, title)}>
              <span>{entry.label}</span>
            </SemanticLabel>
            <strong>{entry.value}</strong>
          </div>
        ))}
      </div>
    </section>
  );
}

function GlossaryList({ entries, title }: { entries: GlossaryEntry[]; title: string }) {
  return (
    <section className="glossary-list">
      <h3>{title}</h3>
      <div className="glossary-rows">
        {entries.map(entry => (
          <article className="glossary-row" key={entry.term}>
            <div>
              <SemanticLabel icon={iconForInfoTerm(entry.term, entry.tag)}><strong>{entry.term}</strong></SemanticLabel>
              <span>{entry.tag}</span>
            </div>
            <p>{entry.description}</p>
          </article>
        ))}
      </div>
    </section>
  );
}
