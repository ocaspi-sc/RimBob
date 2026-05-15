import { DisclosureSection } from '../shared/DisclosureSection';

interface GlossaryEntry {
  term: string;
  tag: string;
  description: string;
}

const rimaiGlossary: GlossaryEntry[] = [
  {
    term: 'Agenda',
    tag: 'Mayor',
    description: 'The living plan: current posture, short-term priorities, long-term goals, and cabinet direction.',
  },
  {
    term: 'AdviceItem',
    tag: 'Cabinet',
    description: 'A feeder minister memo with priority, rationale, suggested actions, resource requests, and citations.',
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
          <h2>INFO</h2>
          <p>Plain-language vocabulary and scope guide. Live metrics belong in ANALYTICS; raw debugging belongs in SYSTEM.</p>
        </div>
        <div className="scope-boundary-strip info-boundary-strip">
          <span>Glossary</span>
          <span>Scope guide</span>
          <span>No live metrics</span>
          <span>No controls</span>
        </div>
      </header>

      <DisclosureSection title="Important buzzwords" defaultOpen meta={`${rimaiGlossary.length + rimworldSignals.length} terms`}>
        <div className="glossary-columns">
          <GlossaryList title="RimAI terms" entries={rimaiGlossary} />
          <GlossaryList title="RimWorld signals" entries={rimworldSignals} />
        </div>
      </DisclosureSection>

      <DisclosureSection title="Where to look" defaultOpen meta="scope guide">
        <div className="analytics-ideas">
          {scopeGuide.map(scope => (
            <article key={scope.name} className="analytics-idea">
              <div>
                <strong>{scope.name}</strong>
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

function GlossaryList({ entries, title }: { entries: GlossaryEntry[]; title: string }) {
  return (
    <section className="glossary-list">
      <h3>{title}</h3>
      <div className="glossary-rows">
        {entries.map(entry => (
          <article className="glossary-row" key={entry.term}>
            <div>
              <strong>{entry.term}</strong>
              <span>{entry.tag}</span>
            </div>
            <p>{entry.description}</p>
          </article>
        ))}
      </div>
    </section>
  );
}
