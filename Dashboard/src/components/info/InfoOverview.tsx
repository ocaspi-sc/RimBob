import { DisclosureSection } from '../shared/DisclosureSection';
import { iconForField, iconForInfoTerm, iconForScope } from '../../dashboard/semanticIcons';
import type { DashboardViewDefinition, DashboardViewKey } from '../../dashboard/scopes';
import { useId, useState, type ReactNode } from 'react';
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
    description: 'Minister-specific LLM material, briefing, rules, infographics, and advice evidence.',
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

interface AlgorithmPort {
  name: string;
  type: string;
  detail: string;
}

interface AlgorithmGroup {
  id: string;
  title: string;
  description: string;
  emoji: string;
}

interface AlgorithmGroupingShape {
  name: string;
  shape: string;
  description: string;
  emoji: string;
}

interface AlgorithmEntry {
  name: string;
  status: 'live' | 'planned';
  group: string;
  family: string;
  owner: string;
  complexity: string;
  inputs: AlgorithmPort[];
  outputs: AlgorithmPort[];
  howItWorks: string;
  context: string;
  sourceLinks: AlgorithmLink[];
  wikipediaLinks: AlgorithmLink[];
  imageKind: 'rect' | 'ring' | 'path' | 'score' | 'diversity' | 'roomAnchor' | 'crop' | 'foodClass' | 'huntRisk' | 'pack' | 'cosine' | 'welfareTriage' | 'sat' | 'distance' | 'cpsat' | 'jps';
}

const algorithmGroups: AlgorithmGroup[] = [
  {
    id: 'willie_live',
    title: 'Live Willie Placement Pipeline',
    description: 'Candidate generation, path scoring, ranking, and selection for current construction advice.',
    emoji: '🏗️',
  },
  {
    id: 'food_knowledge_live',
    title: 'Live Food And Knowledge Runtime',
    description: 'Chef math and local retrieval algorithms that already shape rule and briefing output.',
    emoji: '🌾',
  },
  {
    id: 'welfare_live',
    title: 'Live Welfare Runtime',
    description: 'Mood, need, thought, and room-quality ranking used to prioritize welfare advice.',
    emoji: '🧠',
  },
  {
    id: 'willie_planned',
    title: 'Planned Spatial Accelerators',
    description: 'Reference algorithms being evaluated for cheaper placement gates and later exact solving.',
    emoji: '🧮',
  },
];

const algorithmGroupingShapes: AlgorithmGroupingShape[] = [
  {
    name: 'Pipeline lanes',
    shape: 'current',
    description: 'Group rows by live placement flow, runtime support, and planned accelerators.',
    emoji: '🛤️',
  },
  {
    name: 'Owner bands',
    shape: 'alternate',
    description: 'Separate Willie, Chef, Welfare, and RAG-owned algorithms when debugging minister responsibility.',
    emoji: '👥',
  },
  {
    name: 'Cost classes',
    shape: 'alternate',
    description: 'Sort into constant-time precompute, linear scans, ranked search, and exponential solvers.',
    emoji: '⏱️',
  },
  {
    name: 'Data shapes',
    shape: 'alternate',
    description: 'Cluster grid fields, rectangles, graph paths, vectors, and weighted candidates.',
    emoji: '🧩',
  },
];

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
  riskAssessment: 'https://en.wikipedia.org/wiki/Risk_assessment',
  classification: 'https://en.wikipedia.org/wiki/Statistical_classification',
  centroid: 'https://en.wikipedia.org/wiki/Centroid',
  triage: 'https://en.wikipedia.org/wiki/Triage',
};

function port(name: string, type: string, detail: string): AlgorithmPort {
  return { name, type, detail };
}

const algorithmFieldEmoji = {
  context: '🧭',
  inputs: '📥',
  outputs: '📤',
  runtime: '⏱️',
  source: '🔗',
  wikipedia: '📚',
};

function AlgorithmEmojiLabel({ children, emoji }: { children: ReactNode; emoji: string }) {
  return (
    <span className="algorithm-emoji-label">
      <span aria-hidden="true" className="algorithm-emoji">{emoji}</span>
      <span>{children}</span>
    </span>
  );
}

const algorithms: AlgorithmEntry[] = [
  {
    name: 'Largest empty rectangle search',
    status: 'live',
    group: 'willie_live',
    family: 'Computational geometry',
    owner: 'Willie',
    complexity: 'O(W * H^2 + (WH)^2) worst case after rectangle reduction, O(WH) memory.',
    inputs: [
      port('Map bounds', 'MapBounds', 'Loaded map width, height, and playable coordinates.'),
      port('Occupied cells', 'HashSet<MapCell>', 'Cells blocked by known buildings or approximated occupied points.'),
      port('Freezer footprint size', 'RectSize', 'Exterior shell dimensions that must fit inside free space.'),
      port('Anchor target cell', 'MapCell', 'Kitchen or storage anchor used to order viable rectangles.'),
    ],
    outputs: [
      port('Free rectangles', 'IReadOnlyList<FreeRect>', 'Reduced largest clear rectangles sorted by useful area.'),
      port('Candidate origins', 'IReadOnlyList<MapCell>', 'Top freezer shell origins derived from the selected free rectangles.'),
      port('Scan notes', 'string[]', 'Truncation or occupancy approximation notes attached to the draft.'),
    ],
    howItWorks: 'The generator compresses blocked cells into clear horizontal spans, expands those spans into usable rectangles, and keeps the largest rectangles that can contain the requested freezer shell before converting them into candidate origins near the anchor.',
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
    group: 'willie_live',
    family: 'Template search',
    owner: 'Willie',
    complexity: 'O(A * R^2 * F) before current budgets cap it; A anchors, R radius, F template assets.',
    inputs: [
      port('Resolved anchors', 'IReadOnlyList<ResolvedAnchor>', 'Room or building targets Willie can build near.'),
      port('Search radius', 'int', 'Bounded ring radius around each anchor target cell.'),
      port('Room shell template', 'RoomShell', 'Wall, door, cooler, and floor assets for the requested room.'),
      port('Bounds and occupancy', 'PlacementEvidence', 'In-bounds and blocked-cell evidence used as hard gates.'),
    ],
    outputs: [
      port('Blueprint groups', 'IReadOnlyList<BlueprintGroup>', 'Translated template assets ready for validation.'),
      port('Access cells', 'IReadOnlyList<MapCell>', 'Door and adjacent reachability probe cells.'),
      port('Assumptions', 'string[]', 'Anchor and occupancy assumptions attached to each draft.'),
    ],
    howItWorks: 'The search walks bounded rings around each resolved anchor, rotates the room shell toward the anchor, rejects blocked or out-of-bounds placements, and emits the surviving translated blueprints with door/access metadata.',
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
    name: 'Room anchor inference',
    status: 'live',
    group: 'willie_live',
    family: 'Spatial inference',
    owner: 'Willie',
    complexity: 'O(R + B + C), with R rooms, B buildings, and C room cells scanned for centroids.',
    inputs: [
      port('Room records', 'IReadOnlyList<RoomRecord>', 'Live room rows with role labels, cells, bounds, entry cells, and contained building ids.'),
      port('Building records', 'IReadOnlyList<BuildingRecord>', 'Known buildings grouped by id so rooms can inherit function from their contents.'),
      port('Room class rules', 'RoomClassMapper', 'Role-label and contained-building rules for freezer, kitchen, hospital, bedroom, storage, and workshop anchors.'),
    ],
    outputs: [
      port('Room anchors', 'WillieAnchorInventory', 'Classed room anchors Willie can target during placement and rules evaluation.'),
      port('Centroid', 'MapPosition?', 'Room-cell average or contained-building average used as the anchor point.'),
      port('Footprint evidence', 'MapRect? | IReadOnlyList<MapPosition>', 'Bounds, cells, entry cells, and region id carried forward for placement reasoning.'),
    ],
    howItWorks: 'Willie groups buildings by id, attaches each room to its contained buildings, maps the room label or contents into a room class, and computes a centroid from room cells or building positions so later placement search has named spatial anchors.',
    context: 'Willie uses this to turn raw room and building records into kitchen, freezer, bedroom, hospital, storage, and workshop anchors before scoring construction options.',
    sourceLinks: [
      { label: 'WillieAnchorInventoryDerivation.cs', href: github('Src/StateStore/Derivations/WillieAnchorInventoryDerivation.cs#L8') },
      { label: 'RoomClassMapper.cs', href: github('Src/StateStore/Derivations/Common/RoomClassMapper.cs#L6') },
      { label: 'BuildingClassifier.cs', href: github('Src/StateStore/Derivations/Common/BuildingClassifier.cs#L5') },
    ],
    wikipediaLinks: [
      { label: 'Centroid', href: wiki.centroid },
    ],
    imageKind: 'roomAnchor',
  },
  {
    name: 'Walkable path-cost scoring',
    status: 'live',
    group: 'willie_live',
    family: 'Pathfinding score',
    owner: 'Willie',
    complexity: 'RimBob-side O(P) for P access-cell pairs; external RIMAPI region/A* cost depends on map graph size.',
    inputs: [
      port('Draft access cells', 'IReadOnlyList<MapCell>', 'Cells near doors or openings that pawns must reach.'),
      port('Anchor target cell', 'MapCell', 'Destination cell for the kitchen/storage anchor.'),
      port('Path-cost batch results', 'IReadOnlyList<PathCostResult>', 'RIMAPI reachability and cost results by pair index.'),
      port('Fallback distance', 'int', 'Manhattan distance used when live path probing is unavailable.'),
    ],
    outputs: [
      port('Scored drafts', 'IReadOnlyList<ScoredDraft>', 'Reachable placement drafts with weighted metrics.'),
      port('Distance metric', 'MetricValue', 'Normalized path-cost contribution for the shared scorer.'),
      port('Raw path cost', 'double', 'Underlying path_tiles or tile distance preserved for traces.'),
    ],
    howItWorks: 'Each candidate door/access cell is paired with the relevant anchor and scored with live RIMAPI path-cost data when available; unreachable drafts are filtered and reachable costs are normalized into a placement metric.',
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
    group: 'willie_live',
    family: 'Weighted ranking',
    owner: 'Willie',
    complexity: 'O(D log D + V * M), where D drafts are sorted, V validations are material-scored, and M material rows are summed.',
    inputs: [
      port('Path-cost metric', 'MetricValue', 'Weighted walkability score from the path-cost scorer.'),
      port('Expansion-room metric', 'MetricValue', 'Free-space score estimating future room growth room.'),
      port('Generator confidence', 'MetricValue', 'Prior confidence for template, rectangle, or reuse generators.'),
      port('Material validation cost', 'IReadOnlyList<MaterialEstimate>', 'RIMAPI validation materials summed for readiness.'),
    ],
    outputs: [
      port('Ranked options', 'IReadOnlyList<PlacementOption>', 'Final top one to three player-facing placement choices.'),
      port('Tradeoff note', 'string', 'Short human-readable summary of the ranking tradeoff.'),
      port('Score trace', 'IReadOnlyList<MetricValue>', 'Unit-bearing metric rows shown in solver evidence.'),
    ],
    howItWorks: 'Proposal generators feed drafts into one scorer that applies hard gates first, adds weighted path, material, confidence, expansion, and diversity metrics, then sorts the validated drafts into player-facing options.',
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
    group: 'willie_live',
    family: 'Greedy selection',
    owner: 'Willie',
    complexity: 'O(D * K^2) for D ranked drafts and selected count K; effectively O(D) with the current tiny K.',
    inputs: [
      port('Ranked drafts', 'IReadOnlyList<ScoredDraft>', 'Score-ordered drafts before expensive validation.'),
      port('Selected drafts', 'List<ScoredDraft>', 'Already accepted options compared against each candidate.'),
      port('Anchor ids', 'string[]', 'Anchor identity used to avoid near-duplicate options.'),
      port('Footprint origins', 'MapCell[]', 'Origin and rotation cues used for spatial variety.'),
    ],
    outputs: [
      port('Validation set', 'IReadOnlyList<ScoredDraft>', 'Small diverse set sent to RIMAPI validation.'),
      port('Diversity reason', 'string', 'Trace text explaining why a candidate differs enough.'),
      port('Diversity metric', 'MetricValue', 'Bonus contribution added to the total score.'),
    ],
    howItWorks: 'After the ranked list is built, the selector walks it greedily and keeps candidates that differ from already selected drafts by anchor, orientation, or footprint so expensive validation is spent on meaningfully distinct choices.',
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
    group: 'food_knowledge_live',
    family: 'Yield forecasting',
    owner: 'Chef',
    complexity: 'O(P * B log B); P crop profiles is currently 3, so fertility-band sorting dominates.',
    inputs: [
      port('Colonist count', 'int', 'Pawn count used to size target growing area.'),
      port('Food days', 'float?', 'Current food buffer from the Chef briefing.'),
      port('Crop profiles', 'FoodCropProfile[]', 'Rice, potato, and corn growth/yield constants.'),
      port('Fertility bands', 'FoodTerrainFertilityBand[]', 'Sorted terrain bands used to estimate growth speed.'),
      port('Days to winter', 'float?', 'Season window used for fit and risk.'),
    ],
    outputs: [
      port('Best crop', 'FoodCropCandidate?', 'Highest-ranked crop that fits the current season.'),
      port('Projected nutrition', 'float', 'Expected nutrition from the requested tile count.'),
      port('Food days added', 'float', 'Converted nutrition buffer for the colony.'),
      port('Risk notes', 'string[]', 'Winter, storage, and confidence notes for advice text.'),
    ],
    howItWorks: 'Chef evaluates each supported crop against colony size, fertility bands, days to winter, and storage posture, converts projected yield into nutrition and food-days, and ranks the crop that best fits the current risk window.',
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
    name: 'Food item classification',
    status: 'live',
    group: 'food_knowledge_live',
    family: 'Rule-based classification',
    owner: 'Chef',
    complexity: 'O(N + U log U), with N candidate items and U grouped unclassified food rows.',
    inputs: [
      port('Resource summary', 'ResourceSummary', 'Reported food units, meals, raw-food counts, and total nutrition from the resource snapshot.'),
      port('Stored resources', 'StoredResourceRegistry', 'Stockpile item rows with category, stack count, forbidden state, and position.'),
      port('Map things', 'ThingRegistry', 'Broad map item rows used when stored-resource data is incomplete.'),
      port('Thing definitions', 'ThingDefRegistry', 'Item nutrition, drug, medicine, and category metadata used to classify unknown defs.'),
    ],
    outputs: [
      port('Food classification', 'FoodItemClassification', 'Nutrition source, food units, meals, raw-food count, and item-classification confidence.'),
      port('Food kind labels', 'string?', 'meal, raw_food, or null for non-food item rows.'),
      port('Unclassified samples', 'IReadOnlyList<FoodUnclassifiedItem>', 'Top forbidden or unmatched food-like rows preserved for debugging.'),
    ],
    howItWorks: 'Chef first classifies stored resources, falls back to broad map things if needed, recognizes meals by category/def/label tokens, recognizes raw food by categories or positive nutrition metadata, and estimates nutrition from defs before falling back to meal/raw counts.',
    context: 'Chef uses this to make food-days math robust when RIMAPI reports zero nutrition or when stored resources and broad map things disagree.',
    sourceLinks: [
      { label: 'FoodItemClassifier.cs', href: github('Src/StateStore/Derivations/FoodItemClassifier.cs#L6') },
      { label: 'FoodBriefingDerivation.cs', href: github('Src/StateStore/Derivations/FoodBriefingDerivation.cs#L28') },
    ],
    wikipediaLinks: [
      { label: 'Statistical classification', href: wiki.classification },
    ],
    imageKind: 'foodClass',
  },
  {
    name: 'Hunt-risk profiling',
    status: 'live',
    group: 'food_knowledge_live',
    family: 'Risk classification',
    owner: 'Chef',
    complexity: 'O(A + S log S), with A animals and S grouped species summaries.',
    inputs: [
      port('Animal records', 'IReadOnlyList<AnimalRecord>', 'Wildlife rows with tame, bonded, health, def, id, and position fields.'),
      port('Animal definitions', 'AnimalDefRegistry', 'Predator, explosive, insect, revenge chance, herd/pack, body size, and meat nutrition metadata.'),
      port('Reference point', 'FoodReferencePoint?', 'Colony or storage point used to prefer nearby low-risk targets after profiling.'),
    ],
    outputs: [
      port('Risk profile', 'HuntRiskProfile', 'Risk label, rank, low-risk boolean, estimated nutrition, and reason string.'),
      port('Risk diagnostics', 'HuntRiskDiagnostic', 'Eligibility, metadata, fallback, danger, caution, and nutrition signals for inspection.'),
      port('Hunt summaries', 'IReadOnlyList<WildHuntTarget>', 'Top low-risk species choices ranked for Chef briefing and advice.'),
    ],
    howItWorks: 'Chef blocks tame, bonded, injured, predator, explosive, insect, dangerous-name, high-revenge, or large-body animals, then ranks the remaining species by risk, nutrition, proximity, and count so hunt advice stays bounded and explainable.',
    context: 'Chef uses this before generating hunt advice or assisted-apply hunt rectangles, so the player is not asked to mark dangerous or revenge-heavy animals by default.',
    sourceLinks: [
      { label: 'FoodHuntSafety.cs', href: github('Src/Common/Briefings/FoodHuntSafety.cs#L5') },
      { label: 'FoodBriefingDerivation.cs', href: github('Src/StateStore/Derivations/FoodBriefingDerivation.cs#L494') },
    ],
    wikipediaLinks: [
      { label: 'Risk assessment', href: wiki.riskAssessment },
    ],
    imageKind: 'huntRisk',
  },
  {
    name: 'Bounded harvest and hunt target packing',
    status: 'live',
    group: 'food_knowledge_live',
    family: 'Bounded spatial grouping',
    owner: 'Chef',
    complexity: 'Harvest worst case O(N^3); hunt O(N log N + L*N) for sorted animals and target limit L.',
    inputs: [
      port('Candidate targets', 'PlantRecord[] | AnimalRecord[]', 'Ready plants or low-risk animals after safety filtering.'),
      port('Reference point', 'FoodReferencePoint?', 'Colony or storage point used to order nearby targets.'),
      port('Target limit', 'int', 'Assisted Apply cap for one player-approved designation.'),
      port('Max rectangle area', 'int', 'Blast-radius limit for the resulting map rectangle.'),
    ],
    outputs: [
      port('Apply rectangle', 'MapRect', 'Bounded designation rectangle that the player can inspect.'),
      port('Target ids', 'string[]', 'Exact plant or animal ids included in the action.'),
      port('Target count', 'int', 'Number of targets packed into the rectangle.'),
      port('Proximity label', 'string', 'Nearby/far label for the advice rationale.'),
    ],
    howItWorks: 'Chef filters candidate plants or animals, orders them by safety and proximity, then packs a bounded target set into the smallest safe rectangle that stays under the assisted-apply limits.',
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
    group: 'food_knowledge_live',
    family: 'Vector search',
    owner: 'Mayor and Chef via RAG',
    complexity: 'O(N * E + N log N) for N chunks and embedding dimension E; sorting all scores dominates after dot products.',
    inputs: [
      port('Query embedding', 'float[]', 'Embedding vector for the minister query.'),
      port('Guide embeddings', 'IReadOnlyList<KnowledgeChunk>', 'Local guide chunks with precomputed embeddings.'),
      port('topK', 'int', 'Number of retrieved snippets to include.'),
    ],
    outputs: [
      port('Guide chunks', 'IReadOnlyList<KnowledgeChunk>', 'Top matching guide snippets.'),
      port('Similarity scores', 'float[]', 'Cosine scores used for ordering and traceability.'),
    ],
    howItWorks: 'The retriever embeds the minister query, computes cosine similarity against every cached guide chunk vector, sorts by score, and returns the top snippets as compact prompt context with traceable scores.',
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
    name: 'Welfare triage ranking',
    status: 'live',
    group: 'welfare_live',
    family: 'Priority ranking',
    owner: 'Minister of Welfare',
    complexity: 'O(P log P + T log T + R log R), with P pawns, T low-need or thought rows, and R rooms.',
    inputs: [
      port('Colonist records', 'IReadOnlyList<ColonistRecord>', 'Living colonists with mood, need levels, comfort, beauty, joy, fresh air, and drug desire.'),
      port('Mood thoughts', 'IReadOnlyList<MoodThoughtRecord>', 'Per-pawn thought offsets used to identify the most negative active mood causes.'),
      port('Room records', 'IReadOnlyList<RoomRecord>', 'Room quality stats such as impressiveness, beauty, cleanliness, space, wealth, prison flag, and open roof count.'),
    ],
    outputs: [
      port('Mood summary', 'WelfareMoodSummary', 'Average mood plus break-risk, stressed, and content pawn counts.'),
      port('Worst pawns', 'IReadOnlyList<WelfarePawnMood>', 'Lowest mood pawns with their need levels and top negative thoughts.'),
      port('Need lows', 'IReadOnlyList<WelfareNeedLow>', 'Lowest sleep, comfort, beauty, joy, fresh-air, and drug-desire signals.'),
      port('Worst rooms', 'IReadOnlyList<WelfareRoomQuality>', 'Lowest-quality rooms ranked by impressiveness, beauty, cleanliness, and id.'),
    ],
    howItWorks: 'Welfare filters to living colonists, summarizes the mood distribution, sorts pawns by lowest mood, extracts each pawn\'s strongest negative thoughts and low needs, then separately ranks rooms by poor quality stats so advice can target the worst pressure first.',
    context: 'The Welfare source briefing uses this to turn many pawn and room signals into a small triage packet that future Welfare rules and prompts can inspect quickly.',
    sourceLinks: [
      { label: 'WelfareBriefingDerivation.cs', href: github('Src/StateStore/Derivations/WelfareBriefingDerivation.cs#L7') },
    ],
    wikipediaLinks: [
      { label: 'Triage', href: wiki.triage },
    ],
    imageKind: 'welfareTriage',
  },
  {
    name: 'Summed-area tables',
    status: 'planned',
    group: 'willie_planned',
    family: 'Grid precomputation',
    owner: 'Willie',
    complexity: 'Build O(WH), query O(1), memory O(WH) per map layer.',
    inputs: [
      port('Map layer', 'int[,] | bool[,]', 'Binary or weighted occupancy, roof, filth, danger, or terrain layer.'),
      port('Query bounds', 'MapRect', 'Rectangle tested during placement search.'),
    ],
    outputs: [
      port('Rectangle sum', 'int | float', 'Constant-time aggregate for the requested bounds.'),
      port('Gate result', 'MetricValue | bool', 'Cheap hard gate or soft penalty before validation.'),
    ],
    howItWorks: 'A prefix-sum grid stores the cumulative value above and left of each cell, letting Willie answer any rectangle sum with four array reads instead of rescanning every tile inside the footprint.',
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
    group: 'willie_planned',
    family: 'Distance field',
    owner: 'Willie',
    complexity: 'Usually O(WH) per source layer on grid BFS or distance-field passes; memory O(WH).',
    inputs: [
      port('Map grid', 'MapCell[,]', 'Passable or weighted grid for the current map.'),
      port('Source cells', 'IReadOnlyList<MapCell>', 'Kitchens, freezers, fields, conduits, threats, or edges.'),
      port('Distance model', 'DistanceMetric', 'Manhattan, BFS, or movement-aware metric selection.'),
    ],
    outputs: [
      port('Distance field', 'int[,] | float[,]', 'Reusable nearest-source distance for every cell.'),
      port('Anchor scores', 'MetricValue[]', 'Proximity bonuses or penalties for candidate anchors.'),
      port('Adjacency penalties', 'MetricValue[]', 'Scores for being too close or too far from important layers.'),
    ],
    howItWorks: 'A multi-source pass starts from kitchens, fields, conduits, threats, or other target cells and propagates distance outward so later placement checks can read nearest-source cost directly from the field.',
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
    group: 'willie_planned',
    family: 'Constraint solving',
    owner: 'Willie',
    complexity: 'NP-hard / exponential in the general case; useful only after pruning to a small local candidate set.',
    inputs: [
      port('Room rectangles', 'CandidateRoomRect[]', 'Small pruned set of possible room footprints.'),
      port('Bounds', 'MapRect', 'Local solve area rather than the whole map.'),
      port('Presence variables', 'BoolVar[]', 'Optional room choices in the exact solver.'),
      port('Weighted objective', 'LinearExpr', 'Score expression for compactness, adjacency, and readiness.'),
    ],
    outputs: [
      port('Layout choice', 'PlacementPlan?', 'Exact local room selection and positions.'),
      port('Infeasible result', 'NoFitReason', 'Solver proof that no compatible local layout exists.'),
      port('Tradeoff score', 'double', 'Constraint-backed score for the selected solution.'),
    ],
    howItWorks: 'After cheaper filters shrink the candidate set, the exact solver models each optional room as rectangle variables, adds no-overlap constraints, scores adjacency/readiness objectives, and searches for the best feasible local layout.',
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
    group: 'willie_planned',
    family: 'Pathfinding acceleration',
    owner: 'Willie',
    complexity: 'Worst case O(WH log(WH)) like A*, but often much faster on open uniform grids by pruning symmetric nodes.',
    inputs: [
      port('Uniform-cost grid', 'bool[,]', 'Blocked/passable map layer suitable for symmetry pruning.'),
      port('Start cell', 'MapCell', 'Reachability probe origin.'),
      port('Goal cell', 'MapCell', 'Destination or anchor cell.'),
      port('Blocked layer', 'bool[,]', 'Current obstacles and impassable terrain.'),
    ],
    outputs: [
      port('Path result', 'PathResult', 'Reachable path or no-path answer.'),
      port('Expanded nodes', 'int', 'Smaller explored set than plain A* on open grids.'),
      port('Path cost', 'int | float', 'Movement cost used by later scoring.'),
    ],
    howItWorks: 'Jump Point Search runs like A* on a uniform grid but skips symmetric intermediate cells, jumping to forced neighbors or turning points so open areas require far fewer node expansions.',
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
        <>
          <div className="algorithm-shape-strip" aria-label="Algorithm grouping suggestions">
            {algorithmGroupingShapes.map(shape => (
              <article key={shape.name} className="algorithm-shape-card">
                <AlgorithmEmojiLabel emoji={shape.emoji}><strong>{shape.name}</strong></AlgorithmEmojiLabel>
                <span>{shape.shape}</span>
                <p>{shape.description}</p>
              </article>
            ))}
          </div>

          <div className="algorithm-group-stack">
            {algorithmGroups.map(group => {
              const entries = algorithms.filter(algorithm => algorithm.group === group.id);
              if (entries.length === 0) return null;

              return <AlgorithmGroupSection key={group.id} entries={entries} group={group} />;
            })}
          </div>
        </>
      )}
    </div>
  );
}

function AlgorithmGroupSection({ entries, group }: { entries: AlgorithmEntry[]; group: AlgorithmGroup }) {
  const [open, setOpen] = useState(false);
  const buttonId = useId();
  const panelId = useId();

  return (
    <section className={`algorithm-group ${open ? 'open' : ''}`}>
      <h3 className="algorithm-group-heading">
        <button
          id={buttonId}
          type="button"
          aria-controls={panelId}
          aria-expanded={open}
          className="algorithm-group-button"
          onClick={() => setOpen(value => !value)}
        >
          <span className="algorithm-group-title">
            <AlgorithmEmojiLabel emoji={group.emoji}><strong>{group.title}</strong></AlgorithmEmojiLabel>
            <small>{group.description}</small>
          </span>
          <span className="algorithm-group-count">{entries.length} rows</span>
        </button>
      </h3>
      {open && (
        <div
          id={panelId}
          className="algorithm-group-panel"
          role="region"
          aria-labelledby={buttonId}
        >
          <div className="algorithm-grid">
            {entries.map(algorithm => (
              <AlgorithmCard key={algorithm.name} algorithm={algorithm} />
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

function AlgorithmCard({ algorithm }: { algorithm: AlgorithmEntry }) {
  return (
    <article className="algorithm-card">
      <div className="algorithm-visual">
        <img className="algorithm-image" src={algorithmImageDataUrl(algorithm)} alt={`${algorithm.name} diagram`} loading="lazy" />
      </div>
      <div className="algorithm-card-body">
        <div className="algorithm-card-header">
          <div className="algorithm-title-row">
            <div>
              <h3>{algorithm.name}</h3>
              <p><strong>{algorithm.family}</strong></p>
            </div>
          </div>
        </div>

        <div className="algorithm-links">
          <AlgorithmLinks emoji={algorithmFieldEmoji.source} title="RimBob source" links={algorithm.sourceLinks} />
          <AlgorithmLinks emoji={algorithmFieldEmoji.wikipedia} title="Wikipedia" links={algorithm.wikipediaLinks} />
        </div>

        <p className="algorithm-how"><strong>How it works:</strong> {algorithm.howItWorks}</p>

        <dl className="algorithm-facts">
          <div>
            <dt><AlgorithmEmojiLabel emoji={algorithmFieldEmoji.runtime}><span>Runtime</span></AlgorithmEmojiLabel></dt>
            <dd><strong>{algorithm.complexity}</strong></dd>
          </div>
          <div>
            <dt><AlgorithmEmojiLabel emoji={algorithmFieldEmoji.context}><span>Context</span></AlgorithmEmojiLabel></dt>
            <dd>{algorithm.context}</dd>
          </div>
        </dl>
      </div>

      <div className="algorithm-io">
        <AlgorithmList iconKey="inputs" title="Inputs" items={algorithm.inputs} />
        <AlgorithmList iconKey="outputs" title="Outputs" items={algorithm.outputs} />
      </div>
    </article>
  );
}

function AlgorithmList({ iconKey, items, title }: { iconKey: string; items: AlgorithmPort[]; title: string }) {
  const emoji = iconKey === 'inputs' ? algorithmFieldEmoji.inputs : algorithmFieldEmoji.outputs;

  return (
    <section>
      <h4><AlgorithmEmojiLabel emoji={emoji}><span>{title}</span></AlgorithmEmojiLabel></h4>
      <ul>
        {items.map(item => (
          <li key={`${item.name}-${item.type}`} className="algorithm-port">
            <span aria-hidden="true" className="algorithm-port-emoji">🔹</span>
            <div>
              <span className="algorithm-port-heading">
                <strong>{item.name}</strong>
                <code>{item.type}</code>
              </span>
              <span>{item.detail}</span>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

function AlgorithmLinks({ emoji, links, title }: { emoji: string; links: AlgorithmLink[]; title: string }) {
  return (
    <section>
      <h4><AlgorithmEmojiLabel emoji={emoji}><span>{title}</span></AlgorithmEmojiLabel></h4>
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
    case 'roomAnchor':
      return `
  <g>
    <rect x="58" y="46" width="96" height="88" rx="5" fill="rgba(98,216,230,0.12)" stroke="#62d8e6" stroke-width="3"/>
    <rect x="178" y="42" width="108" height="98" rx="5" fill="rgba(80,211,141,0.13)" stroke="#50d38d" stroke-width="3"/>
    <rect x="80" y="64" width="36" height="18" rx="3" fill="#f0bd5f"/>
    <rect x="204" y="62" width="42" height="20" rx="3" fill="#a78bfa"/>
    <circle cx="106" cy="90" r="8" fill="#f06f7a"/>
    <circle cx="232" cy="92" r="8" fill="#f06f7a"/>
    <path d="M106 90 L164 108 L232 92" fill="none" stroke="#eef5f8" stroke-width="3" stroke-dasharray="5 7"/>
    <circle cx="164" cy="108" r="6" fill="#62d8e6"/>
  </g>`;
    case 'crop':
      return `
  <g>
    <rect x="64" y="116" width="46" height="30" rx="4" fill="#50d38d"/>
    <rect x="132" y="88" width="46" height="58" rx="4" fill="#f0bd5f"/>
    <rect x="200" y="52" width="46" height="94" rx="4" fill="#62d8e6"/>
    <path d="M286 134 C270 108 276 78 306 58 C314 94 306 120 286 134Z" fill="#50d38d"/>
  </g>`;
    case 'foodClass':
      return `
  <g>
    <circle cx="82" cy="58" r="7" fill="#f0bd5f"/>
    <circle cx="104" cy="84" r="7" fill="#50d38d"/>
    <circle cx="76" cy="112" r="7" fill="#a78bfa"/>
    <path d="M112 58 C146 58 148 66 174 74" fill="none" stroke="#62d8e6" stroke-width="3"/>
    <path d="M112 84 C146 84 148 98 174 98" fill="none" stroke="#50d38d" stroke-width="3"/>
    <path d="M112 112 C146 112 148 130 174 124" fill="none" stroke="#a78bfa" stroke-width="3"/>
    <rect x="184" y="52" width="52" height="34" rx="5" fill="rgba(240,189,95,0.2)" stroke="#f0bd5f" stroke-width="2"/>
    <rect x="184" y="92" width="52" height="34" rx="5" fill="rgba(80,211,141,0.2)" stroke="#50d38d" stroke-width="2"/>
    <rect x="184" y="132" width="52" height="20" rx="5" fill="rgba(167,139,250,0.2)" stroke="#a78bfa" stroke-width="2"/>
    <rect x="256" y="62" width="40" height="80" rx="5" fill="rgba(98,216,230,0.1)" stroke="#62d8e6" stroke-width="2" stroke-dasharray="6 5"/>
  </g>`;
    case 'huntRisk':
      return `
  <g>
    <path d="M78 136 A74 74 0 0 1 276 136" fill="none" stroke="#283543" stroke-width="14" stroke-linecap="round"/>
    <path d="M78 136 A74 74 0 0 1 142 70" fill="none" stroke="#50d38d" stroke-width="14" stroke-linecap="round"/>
    <path d="M148 67 A74 74 0 0 1 216 78" fill="none" stroke="#f0bd5f" stroke-width="14" stroke-linecap="round"/>
    <path d="M222 82 A74 74 0 0 1 276 136" fill="none" stroke="#f06f7a" stroke-width="14" stroke-linecap="round"/>
    <path d="M178 132 L224 92" stroke="#eef5f8" stroke-width="5" stroke-linecap="round"/>
    <circle cx="178" cy="132" r="9" fill="#eef5f8"/>
    <circle cx="88" cy="86" r="8" fill="#50d38d"/>
    <circle cx="264" cy="92" r="8" fill="#f06f7a"/>
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
    case 'welfareTriage':
      return `
  <g>
    <rect x="76" y="50" width="188" height="26" rx="5" fill="rgba(240,189,95,0.18)" stroke="#f0bd5f" stroke-width="2"/>
    <rect x="76" y="88" width="148" height="26" rx="5" fill="rgba(98,216,230,0.18)" stroke="#62d8e6" stroke-width="2"/>
    <rect x="76" y="126" width="104" height="26" rx="5" fill="rgba(80,211,141,0.18)" stroke="#50d38d" stroke-width="2"/>
    <circle cx="54" cy="63" r="10" fill="#f06f7a"/>
    <circle cx="54" cy="101" r="10" fill="#f0bd5f"/>
    <circle cx="54" cy="139" r="10" fill="#50d38d"/>
    <path d="M286 54 L292 67 L306 69 L296 79 L298 94 L286 87 L274 94 L276 79 L266 69 L280 67Z" fill="#a78bfa"/>
    <path d="M284 112 C304 104 316 116 310 134 C302 152 266 150 260 132 C254 114 266 104 284 112Z" fill="rgba(98,216,230,0.22)" stroke="#62d8e6" stroke-width="2"/>
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
