import { DisclosureSection } from '../shared/DisclosureSection';
import { iconForField, iconForInfoTerm, iconForScope } from '../../dashboard/semanticIcons';
import type { DashboardViewDefinition, DashboardViewKey } from '../../dashboard/scopes';
import { ViewTabs } from '../layout/ViewTabs';
import { SemanticLabel } from '../shared/SemanticIcon';

interface ConsoleViewProps {
  selectedView: DashboardViewKey;
  views: DashboardViewDefinition[];
  onSelectView: (view: DashboardViewKey) => void;
}

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
    description: 'Colony decisions are split across independent advisors — Chef and Mayor — each with its own briefing, rules, and LLM escalation path.',
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
  { label: 'Ministers', value: '2 (Chef, Mayor)' },
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
    tag: 'Willie',
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
    name: 'Mayor / Chef / Cabinet',
    tag: 'Inspection',
    description: 'Minister-specific prompt, briefing, RAG, rules, raw LLM output, infographics, and advice evidence.',
  },
];

const dataSources = [
  {
    name: 'Live Host health',
    tag: 'SYSTEM',
    description: 'Runtime status, process identity, endpoint coverage, logs, storage roots, and provider state come from /api/system/health.',
  },
  {
    name: 'Live colony snapshot',
    tag: 'Sidebar',
    description: 'The right sidebar reads /api/colony/snapshot and can show restored stale state when RIMAPI is down.',
  },
  {
    name: 'Advice stream',
    tag: 'SSE',
    description: 'Agenda and active advice updates arrive through /api/advice/stream and are kept in the dashboard session buffer.',
  },
  {
    name: 'Static reference copy',
    tag: 'INFO',
    description: 'Glossary and scope guidance are intentionally static so INFO does not become another live metrics page.',
  },
];

interface AlgorithmLink {
  label: string;
  href: string;
}

interface AlgorithmEntry {
  name: string;
  status: 'live' | 'planned';
  family: string;
  owner: string;
  complexity: string;
  inputs: string[];
  outputs: string[];
  context: string;
  sourceLinks: AlgorithmLink[];
  wikipediaLinks: AlgorithmLink[];
  imageKind: 'rect' | 'ring' | 'path' | 'score' | 'diversity' | 'crop' | 'pack' | 'cosine' | 'sat' | 'distance' | 'cpsat' | 'jps';
}

const githubRoot = 'https://github.com/ocaspi-sc/RimBob/blob/master/';

const wiki = {
  largestEmptyRectangle: 'https://en.wikipedia.org/wiki/Largest_empty_rectangle',
  templateMatching: 'https://en.wikipedia.org/wiki/Template_matching',
  aStar: 'https://en.wikipedia.org/wiki/A%2A_search_algorithm',
  multiObjective: 'https://en.wikipedia.org/wiki/Multi-objective_optimization',
  greedy: 'https://en.wikipedia.org/wiki/Greedy_algorithm',
  cropSimulation: 'https://en.wikipedia.org/wiki/Crop_simulation_model',
  rectanglePacking: 'https://en.wikipedia.org/wiki/Rectangle_packing',
  cosine: 'https://en.wikipedia.org/wiki/Cosine_similarity',
  summedAreaTable: 'https://en.wikipedia.org/wiki/Summed-area_table',
  distanceTransform: 'https://en.wikipedia.org/wiki/Distance_transform',
  constraintProgramming: 'https://en.wikipedia.org/wiki/Constraint_programming',
  jumpPointSearch: 'https://en.wikipedia.org/wiki/Jump_point_search',
  taxicab: 'https://en.wikipedia.org/wiki/Taxicab_geometry',
};

const algorithms: AlgorithmEntry[] = [
  {
    name: 'Largest empty rectangle search',
    status: 'live',
    family: 'Computational geometry',
    owner: 'Willie',
    complexity: 'O(W * H^2 + (WH)^2) worst case after rectangle reduction, O(WH) memory.',
    inputs: ['Map bounds', 'Occupied cells', 'Freezer footprint size', 'Anchor target cell'],
    outputs: ['Top free rectangles', 'Candidate freezer origins', 'Rejection notes for truncated scans'],
    context: 'Willie uses this when anchored template placement needs room-sized free space instead of just trying cells around an anchor.',
    sourceLinks: [
      { label: 'PlacementEvidence.cs', href: github('Src/Ministers/Willie/PlacementEvidence.cs#L107') },
      { label: 'LargestEmptyRectangleGenerator.cs', href: github('Src/Ministers/Willie/Generators/LargestEmptyRectangleGenerator.cs#L107') },
    ],
    wikipediaLinks: [
      { label: 'Largest empty rectangle', href: wiki.largestEmptyRectangle },
    ],
    imageKind: 'rect',
  },
  {
    name: 'Anchor-based template search',
    status: 'live',
    family: 'Template search',
    owner: 'Willie',
    complexity: 'O(A * R^2 * F) before current budgets cap it; A anchors, R radius, F template assets.',
    inputs: ['Resolved anchors', 'Search radius', 'Freezer shell template', 'Bounds and occupancy evidence'],
    outputs: ['Translated blueprint groups', 'Door and access cells', 'Template assumptions'],
    context: 'Willie expands rings around kitchen/storage anchors and rotates the freezer shell so the door faces the anchor.',
    sourceLinks: [
      { label: 'TemplateAnchoredGenerator.cs', href: github('Src/Ministers/Willie/Generators/TemplateAnchoredGenerator.cs#L36') },
      { label: 'FreezerTemplate.cs', href: github('Src/Ministers/Willie/Placement/FreezerTemplate.cs#L10') },
    ],
    wikipediaLinks: [
      { label: 'Template matching', href: wiki.templateMatching },
    ],
    imageKind: 'ring',
  },
  {
    name: 'Walkable path-cost scoring',
    status: 'live',
    family: 'Pathfinding score',
    owner: 'Willie',
    complexity: 'RimBob-side O(P) for P access-cell pairs; external RIMAPI region/A* cost depends on map graph size.',
    inputs: ['Draft access cells', 'Anchor target cell', 'RIMAPI path-cost batch results', 'Manhattan fallback distance'],
    outputs: ['Reachable scored drafts', 'Normalized distance metric', 'Raw path cost in path_tiles or tiles'],
    context: 'Willie ranks freezer placements by real walkability to the anchor, falling back to Manhattan distance only when the probe is unavailable.',
    sourceLinks: [
      { label: 'WalkablePathCostScorer.cs', href: github('Src/Ministers/Willie/Placement/WalkablePathCostScorer.cs#L38') },
      { label: 'PathCostLookup.cs', href: github('Src/Ministers/Willie/Placement/PathCostLookup.cs#L24') },
    ],
    wikipediaLinks: [
      { label: 'A* search algorithm', href: wiki.aStar },
      { label: 'Taxicab geometry', href: wiki.taxicab },
    ],
    imageKind: 'path',
  },
  {
    name: 'Multi-objective placement ranking',
    status: 'live',
    family: 'Weighted ranking',
    owner: 'Willie',
    complexity: 'O(D log D + V * M), where D drafts are sorted, V validations are material-scored, and M material rows are summed.',
    inputs: ['Path-cost metric', 'Expansion-room metric', 'Generator confidence', 'Diversity bonus', 'Material validation cost'],
    outputs: ['Ranked top 1-3 options', 'Tradeoff note', 'Unit-bearing score trace'],
    context: 'Willie keeps generators as proposal sources and lets one shared judge combine hard gates, weighted metrics, validation, and final ordering.',
    sourceLinks: [
      { label: 'PlacementSolver.cs', href: github('Src/Ministers/Willie/PlacementSolver.cs#L250') },
      { label: 'ScoreWeights.cs', href: github('Src/Ministers/Willie/Placement/ScoreWeights.cs#L1') },
    ],
    wikipediaLinks: [
      { label: 'Multi-objective optimization', href: wiki.multiObjective },
    ],
    imageKind: 'score',
  },
  {
    name: 'Greedy diversity selection',
    status: 'live',
    family: 'Greedy selection',
    owner: 'Willie',
    complexity: 'O(D * K^2) for D ranked drafts and selected count K; effectively O(D) with the current tiny K.',
    inputs: ['Ranked scored drafts', 'Already selected drafts', 'Anchor ids', 'Door rotations', 'Footprint origins'],
    outputs: ['Diverse validation set', 'Diversity reason', 'Diversity score contribution'],
    context: 'Willie avoids validating three near-identical freezer candidates by rewarding different anchors, orientations, or origins.',
    sourceLinks: [
      { label: 'DiverseSelector.cs', href: github('Src/Ministers/Willie/Placement/DiverseSelector.cs#L9') },
      { label: 'DraftDedupe.cs', href: github('Src/Ministers/Willie/Placement/DraftDedupe.cs#L74') },
    ],
    wikipediaLinks: [
      { label: 'Greedy algorithm', href: wiki.greedy },
    ],
    imageKind: 'diversity',
  },
  {
    name: 'Crop forecast scoring',
    status: 'live',
    family: 'Yield forecasting',
    owner: 'Chef',
    complexity: 'O(P * B log B); P crop profiles is currently 3, so fertility-band sorting dominates.',
    inputs: ['Colonist count', 'Food days', 'Crop profiles', 'Terrain fertility bands', 'Days to winter', 'Storage posture'],
    outputs: ['Best crop candidate', 'Projected nutrition', 'Projected food days added', 'Winter margin', 'Confidence and storage multipliers'],
    context: 'Chef explains why rice, potatoes, or corn fits the current food and season window instead of hardcoding one crop forever.',
    sourceLinks: [
      { label: 'FoodCropMath.cs', href: github('Src/Ministers/Food/FoodCropMath.cs#L57') },
      { label: 'FoodBriefingDerivation.cs', href: github('Src/StateStore/Derivations/FoodBriefingDerivation.cs#L664') },
    ],
    wikipediaLinks: [
      { label: 'Crop simulation model', href: wiki.cropSimulation },
    ],
    imageKind: 'crop',
  },
  {
    name: 'Bounded harvest and hunt target packing',
    status: 'live',
    family: 'Bounded spatial grouping',
    owner: 'Chef',
    complexity: 'Harvest worst case O(N^3); hunt O(N log N + L*N) for sorted animals and target limit L.',
    inputs: ['Candidate plants or animals', 'Reference point', 'Apply target limit', 'Maximum allowed rectangle area'],
    outputs: ['Apply-safe map rectangle', 'Target ids', 'Target count', 'Proximity label'],
    context: 'Chef turns many possible harvest or hunt targets into a small rectangular designation that the player can inspect and apply safely.',
    sourceLinks: [
      { label: 'Harvest target selection', href: github('Src/StateStore/Derivations/FoodBriefingDerivation.cs#L298') },
      { label: 'Hunt target selection', href: github('Src/StateStore/Derivations/FoodBriefingDerivation.cs#L590') },
    ],
    wikipediaLinks: [
      { label: 'Rectangle packing', href: wiki.rectanglePacking },
    ],
    imageKind: 'pack',
  },
  {
    name: 'Cosine similarity retrieval',
    status: 'live',
    family: 'Vector search',
    owner: 'Mayor and Chef via RAG',
    complexity: 'O(N * E + N log N) for N chunks and embedding dimension E; sorting all scores dominates after dot products.',
    inputs: ['Query embedding', 'Guide chunk embeddings', 'topK'],
    outputs: ['Top matching guide chunks', 'Similarity scores used for ordering'],
    context: 'RAG uses this in-process store to retrieve guide snippets without a vector database.',
    sourceLinks: [
      { label: 'KnowledgeBase.cs', href: github('Src/KnowledgeBase/KnowledgeBase.cs#L39') },
    ],
    wikipediaLinks: [
      { label: 'Cosine similarity', href: wiki.cosine },
    ],
    imageKind: 'cosine',
  },
  {
    name: 'Summed-area tables',
    status: 'planned',
    family: 'Grid precomputation',
    owner: 'Willie',
    complexity: 'Build O(WH), query O(1), memory O(WH) per map layer.',
    inputs: ['Binary or weighted map layer', 'Rectangular query bounds'],
    outputs: ['Constant-time rectangle sums', 'Cheap hard-gate and soft-penalty checks'],
    context: 'Planned for Willie buildability and danger/filth/roof checks before expensive validation.',
    sourceLinks: [
      { label: 'summed-area-tables.md', href: github('Docs/placement_algorithms/summed-area-tables.md#L1') },
      { label: 'placement README', href: github('Docs/placement_algorithms/README.md#L31') },
    ],
    wikipediaLinks: [
      { label: 'Summed-area table', href: wiki.summedAreaTable },
    ],
    imageKind: 'sat',
  },
  {
    name: 'Distance transforms',
    status: 'planned',
    family: 'Distance field',
    owner: 'Willie',
    complexity: 'Usually O(WH) per source layer on grid BFS or distance-field passes; memory O(WH).',
    inputs: ['Map grid', 'Target source cells', 'Distance metric or movement model'],
    outputs: ['Distance field', 'Anchor scores', 'Adjacency penalties or bonuses'],
    context: 'Planned to turn kitchen/freezer/field/conduit/threat proximity into reusable fields instead of repeated local scans.',
    sourceLinks: [
      { label: 'distance-transforms.md', href: github('Docs/placement_algorithms/distance-transforms.md#L1') },
      { label: 'placement README', href: github('Docs/placement_algorithms/README.md#L34') },
    ],
    wikipediaLinks: [
      { label: 'Distance transform', href: wiki.distanceTransform },
    ],
    imageKind: 'distance',
  },
  {
    name: 'CP-SAT NoOverlap2D',
    status: 'planned',
    family: 'Constraint solving',
    owner: 'Willie',
    complexity: 'NP-hard / exponential in the general case; useful only after pruning to a small local candidate set.',
    inputs: ['Candidate room rectangles', 'Bounds', 'Presence variables', 'No-overlap constraints', 'Weighted objective'],
    outputs: ['Exact local layout choice', 'Infeasible result', 'Constraint-backed tradeoff score'],
    context: 'Planned as a later exact local solver for multi-room placement after cheaper generators shrink the search space.',
    sourceLinks: [
      { label: 'cp-sat-no-overlap-2d.md', href: github('Docs/placement_algorithms/cp-sat-no-overlap-2d.md#L1') },
      { label: 'construction design', href: github('Docs/design/ministers/construction.md#L117') },
    ],
    wikipediaLinks: [
      { label: 'Constraint programming', href: wiki.constraintProgramming },
      { label: 'Rectangle packing', href: wiki.rectanglePacking },
    ],
    imageKind: 'cpsat',
  },
  {
    name: 'Jump Point Search',
    status: 'planned',
    family: 'Pathfinding acceleration',
    owner: 'Willie',
    complexity: 'Worst case O(WH log(WH)) like A*, but often much faster on open uniform grids by pruning symmetric nodes.',
    inputs: ['Uniform-cost grid', 'Start cell', 'Goal cell', 'Blocked/passable map layer'],
    outputs: ['Reachable path or no-path result', 'Fewer expanded nodes than plain A* in open grids'],
    context: 'Planned for repeated grid reachability checks after candidates survive cheaper filters.',
    sourceLinks: [
      { label: 'jump-point-search.md', href: github('Docs/placement_algorithms/jump-point-search.md#L1') },
      { label: 'placement README', href: github('Docs/placement_algorithms/README.md#L39') },
    ],
    wikipediaLinks: [
      { label: 'Jump Point Search', href: wiki.jumpPointSearch },
      { label: 'A* search algorithm', href: wiki.aStar },
    ],
    imageKind: 'jps',
  },
];

export function InfoOverview({
  selectedView,
  views,
  onSelectView,
}: ConsoleViewProps) {
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

      <ViewTabs
        activeView={selectedView}
        ariaLabel="INFO reference views"
        views={views}
        onSelect={onSelectView}
      />

      {/* ── Highlights ─────────────────────────────────────────────────── */}
      {selectedView === 'overview' && (
        <>
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
        </>
      )}

      {selectedView === 'contracts' && (
        <>
      <DisclosureSection title={<SemanticLabel icon={iconForField('rules')}><span>Design decisions</span></SemanticLabel>} defaultOpen meta={`${designDecisions.length} choices`}>
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
        </>
      )}

      {selectedView === 'glossary' && (
      <DisclosureSection title={<SemanticLabel icon={iconForField('info')}><span>Important buzzwords</span></SemanticLabel>} defaultOpen meta={`${rimbobGlossary.length + rimworldSignals.length} terms`}>
        <div className="glossary-columns">
          <GlossaryList title="RimBob terms" entries={rimbobGlossary} />
          <GlossaryList title="RimWorld signals" entries={rimworldSignals} />
        </div>
      </DisclosureSection>

      )}

      {selectedView === 'data_sources' && (
        <>
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
      <DisclosureSection title={<SemanticLabel icon={iconForField('source')}><span>Data sources</span></SemanticLabel>} defaultOpen meta={`${dataSources.length} sources`}>
        <div className="analytics-ideas">
          {dataSources.map(source => (
            <article key={source.name} className="analytics-idea">
              <div>
                <SemanticLabel icon={iconForInfoTerm(source.name, source.tag)}><strong>{source.name}</strong></SemanticLabel>
                <span>{source.tag}</span>
              </div>
              <p>{source.description}</p>
            </article>
          ))}
        </div>
      </DisclosureSection>
        </>
      )}

      {selectedView === 'algorithms' && (
        <DisclosureSection title={<SemanticLabel icon={iconForField('algorithm')}><span>Algorithms</span></SemanticLabel>} defaultOpen meta={`${algorithms.length} panels`}>
          <div className="algorithm-grid">
            {algorithms.map(algorithm => (
              <AlgorithmCard key={algorithm.name} algorithm={algorithm} />
            ))}
          </div>
        </DisclosureSection>
      )}
    </div>
  );
}

function AlgorithmCard({ algorithm }: { algorithm: AlgorithmEntry }) {
  return (
    <article className="algorithm-card">
      <div className="algorithm-card-header">
        <div>
          <span className={`algorithm-status algorithm-status-${algorithm.status}`}>{algorithm.status}</span>
          <h3><SemanticLabel icon={iconForInfoTerm(algorithm.name, algorithm.family)}><span>{algorithm.name}</span></SemanticLabel></h3>
          <p>{algorithm.family}</p>
        </div>
        <img className="algorithm-image" src={algorithmImageDataUrl(algorithm)} alt={`${algorithm.name} diagram`} loading="lazy" />
      </div>

      <dl className="algorithm-facts">
        <div>
          <dt>Owner</dt>
          <dd>{algorithm.owner}</dd>
        </div>
        <div>
          <dt>Runtime</dt>
          <dd>{algorithm.complexity}</dd>
        </div>
        <div>
          <dt>Context</dt>
          <dd>{algorithm.context}</dd>
        </div>
      </dl>

      <div className="algorithm-io">
        <AlgorithmList title="Inputs" items={algorithm.inputs} />
        <AlgorithmList title="Outputs" items={algorithm.outputs} />
      </div>

      <div className="algorithm-links">
        <AlgorithmLinks title="RimBob source" links={algorithm.sourceLinks} />
        <AlgorithmLinks title="Wikipedia" links={algorithm.wikipediaLinks} />
      </div>
    </article>
  );
}

function AlgorithmList({ items, title }: { items: string[]; title: string }) {
  return (
    <section>
      <h4>{title}</h4>
      <ul>
        {items.map(item => <li key={item}>{item}</li>)}
      </ul>
    </section>
  );
}

function AlgorithmLinks({ links, title }: { links: AlgorithmLink[]; title: string }) {
  return (
    <section>
      <h4>{title}</h4>
      <div>
        {links.map(link => (
          <a key={link.href} href={link.href} target="_blank" rel="noreferrer">{link.label}</a>
        ))}
      </div>
    </section>
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

function github(path: string): string {
  return `${githubRoot}${path}`;
}

function algorithmImageDataUrl(algorithm: AlgorithmEntry): string {
  const title = escapeSvg(algorithm.name);
  const subtitle = escapeSvg(algorithm.family);
  const art = algorithmArt(algorithm.imageKind);
  const svg = `
<svg xmlns="http://www.w3.org/2000/svg" width="360" height="210" viewBox="0 0 360 210" role="img">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#10243a"/>
      <stop offset="1" stop-color="#171a25"/>
    </linearGradient>
    <linearGradient id="accent" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#62d8e6"/>
      <stop offset="1" stop-color="#50d38d"/>
    </linearGradient>
  </defs>
  <rect width="360" height="210" rx="14" fill="url(#bg)"/>
  <rect x="14" y="14" width="332" height="182" rx="10" fill="#0b1018" stroke="#344050"/>
  ${art}
  <text x="22" y="178" fill="#eef5f8" font-family="Segoe UI, sans-serif" font-size="16" font-weight="700">${title}</text>
  <text x="22" y="194" fill="#8fa0ad" font-family="Segoe UI, sans-serif" font-size="11">${subtitle}</text>
</svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}

function algorithmArt(kind: AlgorithmEntry['imageKind']): string {
  switch (kind) {
    case 'rect':
      return `
  <g opacity="0.9">
    ${gridSvg()}
    <rect x="100" y="54" width="134" height="82" rx="4" fill="rgba(80,211,141,0.18)" stroke="#50d38d" stroke-width="3"/>
    <circle cx="74" cy="62" r="5" fill="#f06f7a"/>
    <circle cx="252" cy="94" r="5" fill="#f06f7a"/>
    <circle cx="142" cy="132" r="5" fill="#f06f7a"/>
  </g>`;
    case 'ring':
      return `
  <g fill="none" stroke="#62d8e6" stroke-width="2">
    <circle cx="174" cy="88" r="10" fill="#50d38d" stroke="none"/>
    <rect x="136" y="50" width="76" height="76" rx="8"/>
    <rect x="112" y="26" width="124" height="124" rx="12" opacity="0.55"/>
    <path d="M174 88 L234 88" stroke="#f0bd5f" stroke-width="4"/>
    <rect x="226" y="76" width="42" height="24" rx="4" fill="rgba(240,189,95,0.16)"/>
  </g>`;
    case 'path':
      return `
  <g>
    ${gridSvg()}
    <path d="M52 132 C88 126 88 76 126 78 S168 132 210 112 S236 54 286 58" fill="none" stroke="#f0bd5f" stroke-width="6" stroke-linecap="round"/>
    <circle cx="52" cy="132" r="8" fill="#50d38d"/>
    <circle cx="286" cy="58" r="8" fill="#f06f7a"/>
  </g>`;
    case 'score':
      return `
  <g>
    <rect x="68" y="112" width="42" height="34" rx="4" fill="#62d8e6"/>
    <rect x="128" y="82" width="42" height="64" rx="4" fill="#50d38d"/>
    <rect x="188" y="56" width="42" height="90" rx="4" fill="#f0bd5f"/>
    <rect x="248" y="96" width="42" height="50" rx="4" fill="#a78bfa"/>
    <path d="M58 148 H304" stroke="#526173" stroke-width="2"/>
  </g>`;
    case 'diversity':
      return `
  <g>
    <circle cx="84" cy="68" r="14" fill="#50d38d"/>
    <circle cx="150" cy="112" r="14" fill="#62d8e6"/>
    <circle cx="224" cy="58" r="14" fill="#f0bd5f"/>
    <circle cx="260" cy="130" r="14" fill="#a78bfa"/>
    <path d="M84 68 L150 112 L224 58 L260 130" fill="none" stroke="#526173" stroke-width="3" stroke-dasharray="6 6"/>
  </g>`;
    case 'crop':
      return `
  <g>
    <rect x="64" y="116" width="46" height="30" rx="4" fill="#50d38d"/>
    <rect x="132" y="88" width="46" height="58" rx="4" fill="#f0bd5f"/>
    <rect x="200" y="52" width="46" height="94" rx="4" fill="#62d8e6"/>
    <path d="M286 134 C270 108 276 78 306 58 C314 94 306 120 286 134Z" fill="#50d38d"/>
  </g>`;
    case 'pack':
      return `
  <g>
    <rect x="66" y="42" width="220" height="112" rx="6" fill="rgba(98,216,230,0.08)" stroke="#62d8e6" stroke-width="3"/>
    <rect x="86" y="62" width="46" height="26" rx="3" fill="#50d38d"/>
    <rect x="142" y="64" width="36" height="50" rx="3" fill="#f0bd5f"/>
    <rect x="190" y="70" width="70" height="28" rx="3" fill="#a78bfa"/>
    <rect x="92" y="106" width="68" height="32" rx="3" fill="#62d8e6"/>
  </g>`;
    case 'cosine':
      return `
  <g stroke-linecap="round">
    <path d="M96 142 L252 58" stroke="#62d8e6" stroke-width="7"/>
    <path d="M96 142 L276 120" stroke="#50d38d" stroke-width="7"/>
    <path d="M150 112 C172 128 204 132 232 124" fill="none" stroke="#f0bd5f" stroke-width="4"/>
    <circle cx="96" cy="142" r="7" fill="#eef5f8"/>
  </g>`;
    case 'sat':
      return `
  <g>
    ${gridSvg()}
    <rect x="92" y="58" width="148" height="82" rx="5" fill="rgba(240,189,95,0.18)" stroke="#f0bd5f" stroke-width="3"/>
    <circle cx="92" cy="58" r="5" fill="#62d8e6"/>
    <circle cx="240" cy="58" r="5" fill="#62d8e6"/>
    <circle cx="92" cy="140" r="5" fill="#62d8e6"/>
    <circle cx="240" cy="140" r="5" fill="#62d8e6"/>
  </g>`;
    case 'distance':
      return `
  <g fill="none" stroke-width="3">
    <circle cx="178" cy="94" r="22" stroke="#50d38d"/>
    <circle cx="178" cy="94" r="48" stroke="#62d8e6" opacity="0.75"/>
    <circle cx="178" cy="94" r="74" stroke="#a78bfa" opacity="0.55"/>
    <circle cx="178" cy="94" r="7" fill="#f0bd5f" stroke="none"/>
  </g>`;
    case 'cpsat':
      return `
  <g>
    <rect x="64" y="48" width="88" height="62" rx="5" fill="#62d8e6"/>
    <rect x="176" y="42" width="64" height="96" rx="5" fill="#50d38d"/>
    <rect x="254" y="88" width="50" height="50" rx="5" fill="#f0bd5f"/>
    <path d="M152 78 H176 M240 90 H254" stroke="#eef5f8" stroke-width="4" stroke-dasharray="5 7"/>
  </g>`;
    case 'jps':
      return `
  <g>
    ${gridSvg()}
    <path d="M54 138 H132 V76 H232 V48 H292" fill="none" stroke="#50d38d" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="M92 138 H118 M164 76 H206 M252 48 H278" stroke="#f0bd5f" stroke-width="3" stroke-dasharray="4 7"/>
    <circle cx="54" cy="138" r="7" fill="#62d8e6"/>
    <circle cx="292" cy="48" r="7" fill="#f06f7a"/>
  </g>`;
  }
}

function gridSvg(): string {
  return `
  <g stroke="#293442" stroke-width="1">
    <path d="M52 42 H300 M52 66 H300 M52 90 H300 M52 114 H300 M52 138 H300"/>
    <path d="M52 42 V138 M76 42 V138 M100 42 V138 M124 42 V138 M148 42 V138 M172 42 V138 M196 42 V138 M220 42 V138 M244 42 V138 M268 42 V138 M292 42 V138"/>
  </g>`;
}

function escapeSvg(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
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
