import type { CSSProperties } from 'react';
import type { AdviceChainModel, AdviceChainPath, AdviceChainStep, AdviceChainStepStatus } from '../../types/advice';
import { iconForField } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from './SemanticIcon';

const ACTION_REQUESTED = '\u26a1 Action Requested';

export function ChainTable({ model }: { model: AdviceChainModel }) {
  if (!model.paths.length) return null;

  const nodes = buildDiagramNodes(model.paths);

  return (
    <div className="chain-diagram-stack" aria-label="Food chain diagram">
      <section className="chain-diagram-panel chain-diagram--orders">
        <header className="chain-diagram-heading">
          <div>
            <span className="chain-diagram-kicker">Food chain</span>
            <h3>Work Order Flow</h3>
          </div>
          <p>Grow, forage, and hunt merge into the same storage, cooking, and cold-chain path.</p>
        </header>
        <FlowCanvas nodes={nodes} />
      </section>
    </div>
  );
}

function FlowCanvas({ nodes }: { nodes: DiagramNode[] }) {
  return (
    <div className="food-flow food-flow--orders">
      <svg aria-hidden className="food-flow-wires" preserveAspectRatio="none" viewBox="0 0 100 100">
        <defs>
          <marker id="arrow-orders" markerHeight="7" markerWidth="7" orient="auto" refX="6" refY="3.5">
            <path d="M0,0 L7,3.5 L0,7 Z" />
          </marker>
        </defs>
        {FLOW_EDGES.map(edge => (
          <path
            className={`food-flow-wire food-flow-wire--${edge.route}`}
            d={wirePath(edge)}
            key={`${edge.from}-${edge.to}`}
            markerEnd="url(#arrow-orders)"
          />
        ))}
      </svg>
      {nodes.map(node => (
        <FlowNode key={node.id} node={node} />
      ))}
    </div>
  );
}

function FlowNode({ node }: { node: DiagramNode }) {
  const style = {
    '--node-x': `${node.x}%`,
    '--node-y': `${node.y}%`,
  } as CSSProperties;

  return (
    <article className={`food-flow-node food-flow-node--${node.route} chain-fact--${classFor(node.status)}`} style={style}>
      <SemanticIconCue className="chain-fact-icon" icon={iconForField(node.iconKey)} size="sm" />
      <div>
        <h4>{node.title}</h4>
        <p>
          <StateText status={node.status} text={node.detail} />
        </p>
      </div>
    </article>
  );
}

type DiagramRoute = 'common' | 'forage' | 'grow' | 'hunt';
type RouteKind = 'forage' | 'grow' | 'hunt';

type DiagramNode = {
  detail: string;
  iconKey: string;
  id: DiagramNodeId;
  route: DiagramRoute;
  status: AdviceChainStepStatus;
  title: string;
  x: number;
  y: number;
};

type DiagramNodeId =
  | 'butcher'
  | 'cook'
  | 'fridge'
  | 'harvest'
  | 'hunt'
  | 'plant'
  | 'storage'
  | 'targets'
  | 'wild'
  | 'zone';

type FlowEdge = {
  from: DiagramNodeId;
  route: DiagramRoute;
  to: DiagramNodeId;
};

const NODE_POSITIONS: Record<DiagramNodeId, { x: number; y: number }> = {
  zone: { x: 8, y: 18 },
  plant: { x: 24, y: 18 },
  wild: { x: 24, y: 45 },
  harvest: { x: 44, y: 31 },
  targets: { x: 8, y: 74 },
  hunt: { x: 24, y: 74 },
  butcher: { x: 40, y: 74 },
  storage: { x: 60, y: 45 },
  cook: { x: 76, y: 45 },
  fridge: { x: 92, y: 45 },
};

const FLOW_EDGES: FlowEdge[] = [
  { from: 'zone', to: 'plant', route: 'grow' },
  { from: 'plant', to: 'harvest', route: 'grow' },
  { from: 'wild', to: 'harvest', route: 'forage' },
  { from: 'harvest', to: 'storage', route: 'common' },
  { from: 'targets', to: 'hunt', route: 'hunt' },
  { from: 'hunt', to: 'butcher', route: 'hunt' },
  { from: 'butcher', to: 'storage', route: 'hunt' },
  { from: 'storage', to: 'cook', route: 'common' },
  { from: 'cook', to: 'fridge', route: 'common' },
];

function buildDiagramNodes(paths: AdviceChainPath[]): DiagramNode[] {
  const grow = pathByRoute(paths, 'grow');
  const forage = pathByRoute(paths, 'forage');
  const hunt = pathByRoute(paths, 'hunt');

  const growZone = findStep(grow, 'grow.zone');
  const growHarvest = findStep(grow, 'grow.harvest');
  const forageHarvest = findStep(forage, 'forage.harvest');
  const huntStep = findStep(hunt, 'hunt.hunt');
  const butcher = findStep(hunt, 'hunt.butcher');
  const storage = mostUrgentStep([
    findStep(grow, 'grow.store'),
    findStep(forage, 'forage.store'),
    findStep(hunt, 'hunt.store'),
  ]);
  const cook = mostUrgentStep([
    findStep(grow, 'grow.cook'),
    findStep(forage, 'forage.cook'),
    findStep(hunt, 'hunt.cook'),
  ]);

  return [
    node('zone', 'Zone', compactDetail(growZone?.detail, 'grow area'), growZone?.status ?? 'available', 'grow', 'crop_zone_summaries'),
    node('plant', 'Plant', compactDetail(growHarvest?.detail, 'crops growing'), growHarvest ? 'have' : 'available', 'grow', 'crop_breakdown'),
    node('wild', 'Wild Plants', compactDetail(forageHarvest?.detail, 'berries visible'), forageHarvest?.status ?? 'available', 'forage', 'wild_harvest_clusters'),
    node('harvest', 'Harvest', mergedActionDetail([growHarvest, forageHarvest]), mostUrgentStatus([growHarvest, forageHarvest]), 'common', 'mark_harvest'),
    node('targets', 'Targets', compactDetail(huntStep?.detail, 'animals visible'), currentSupplyStatus(huntStep), 'hunt', 'wild_animal_count'),
    node('hunt', 'Hunt', statusPhrase(huntStep), huntStep?.status ?? 'available', 'hunt', 'mark_hunt'),
    node('butcher', 'Butcher', compactDetail(butcher?.detail, 'butcher table'), butcher?.status ?? 'available', 'hunt', 'kitchen_and_butchery'),
    node('storage', 'Storage', compactDetail(storage?.detail, 'stockpile'), storage?.status ?? 'available', 'common', 'storage'),
    node('cook', 'Cook', detailOrStatus(cook), cook?.status ?? 'available', 'common', 'production_bill'),
    node('fridge', 'Fridge', refrigeratorDetail(storage), refrigeratorStatus(storage), 'common', 'storage'),
  ];
}

function node(
  id: DiagramNodeId,
  title: string,
  detail: string,
  status: AdviceChainStepStatus,
  route: DiagramRoute,
  iconKey: string,
): DiagramNode {
  const position = NODE_POSITIONS[id];
  return { detail, iconKey, id, route, status, title, x: position.x, y: position.y };
}

function StateText({ status, text }: { status: AdviceChainStepStatus; text: string }) {
  if (status === 'action') return <strong>{text}</strong>;
  return <>{text}</>;
}

function wirePath(edge: FlowEdge): string {
  const from = NODE_POSITIONS[edge.from];
  const to = NODE_POSITIONS[edge.to];
  const bend = Math.abs(from.y - to.y) > 8 ? ` C ${from.x + 6},${from.y} ${to.x - 6},${to.y}` : ' L';
  return `M ${from.x},${from.y}${bend} ${to.x},${to.y}`;
}

function pathByRoute(paths: AdviceChainPath[], route: RouteKind): AdviceChainPath | undefined {
  return paths.find(path => routeKind(path) === route);
}

function routeKind(path: AdviceChainPath): RouteKind {
  const name = path.name.toLowerCase();
  if (name.includes('hunt')) return 'hunt';
  if (name.includes('forage')) return 'forage';
  return 'grow';
}

function classFor(status: AdviceChainStepStatus): AdviceChainStepStatus {
  return status;
}

function mostUrgentStep(steps: Array<AdviceChainStep | undefined>): AdviceChainStep | undefined {
  return steps
    .filter((step): step is AdviceChainStep => step !== undefined)
    .sort((left, right) => statusRank(right.status) - statusRank(left.status))[0];
}

function mostUrgentStatus(steps: Array<AdviceChainStep | undefined>): AdviceChainStepStatus {
  return mostUrgentStep(steps)?.status ?? 'available';
}

function statusRank(status: AdviceChainStepStatus): number {
  if (status === 'action') return 5;
  if (status === 'blocked') return 4;
  if (status === 'trigger') return 3;
  if (status === 'available') return 2;
  return 1;
}

function currentSupplyStatus(step: AdviceChainStep | undefined): AdviceChainStepStatus {
  if (!step) return 'available';
  if (step.status === 'blocked' || step.status === 'trigger') return step.status;
  return 'have';
}

function findStep(path: AdviceChainPath | undefined, key: string): AdviceChainStep | undefined {
  return path?.steps.find(step => step.key === key);
}

function detailOrStatus(step: AdviceChainStep | undefined): string {
  if (!step) return 'pending';
  if (step.status === 'action') return ACTION_REQUESTED;
  if (step.status === 'blocked') return cleanBlockedDetail(step.detail);
  return compactDetail(step.detail, 'ready');
}

function statusPhrase(step: AdviceChainStep | undefined): string {
  if (!step) return 'pending';
  if (step.status === 'action') return ACTION_REQUESTED;
  if (step.status === 'blocked') return cleanBlockedDetail(step.detail);
  if (step.status === 'available') return 'available';
  if (step.status === 'have') return 'covered';
  return 'pressure';
}

function mergedActionDetail(steps: Array<AdviceChainStep | undefined>): string {
  const urgent = mostUrgentStep(steps);
  if (!urgent) return 'pending';
  if (urgent.status === 'action') return ACTION_REQUESTED;
  if (urgent.status === 'blocked') return cleanBlockedDetail(urgent.detail);
  return compactDetail(urgent.detail, statusPhrase(urgent));
}

function refrigeratorStatus(storage: AdviceChainStep | undefined): AdviceChainStepStatus {
  if (!storage) return 'available';
  return storage.detail.toLowerCase().includes('no cooler') ? 'blocked' : storage.status;
}

function refrigeratorDetail(storage: AdviceChainStep | undefined): string {
  if (!storage) return 'future cold room';
  if (storage.detail.toLowerCase().includes('no cooler')) return 'no cooler';
  if (storage.status === 'have') return 'cold storage';
  return compactDetail(storage.detail, 'future cold room');
}

function compactDetail(value: string | undefined, fallback: string): string {
  if (!value || value.trim() === '') return fallback;
  const cleaned = cleanBlockedDetail(value)
    .replace(/\s*\([^)]{18,}\)/g, '')
    .replace(/\s+/g, ' ')
    .replace(/\b(\d+) crop tiles in (\d+) growing area\b/i, '$1 tiles / $2 zone')
    .replace(/\b(\d+) ([a-z]+) plants at (\d+(?:\.\d+)?)%/i, '$1 $2 / $3%')
    .replace(/\b(\d+) berry plants\b/i, '$1 berries')
    .replace(/\b(\d+) stockpile zones; no cooler\b/i, '$1 zones / no cooler')
    .replace(/\b(\d+) butcher table\b/i, '$1 table')
    .replace(/\)+$/g, '')
    .trim();
  if (cleaned.length <= 34) return cleaned;
  const boundary = cleaned.slice(0, 34).lastIndexOf(' ');
  return `${cleaned.slice(0, boundary > 18 ? boundary : 34)}...`;
}

function cleanBlockedDetail(detail: string): string {
  return detail
    .replace(/^no food /i, 'no ')
    .replace(/^no /i, 'no ')
    .replace(/ visible$/i, '');
}
