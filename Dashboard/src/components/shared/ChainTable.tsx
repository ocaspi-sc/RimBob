import type { AdviceChainModel, AdviceChainPath, AdviceChainStep, AdviceChainStepStatus } from '../../types/advice';
import { iconForField } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from './SemanticIcon';

const ACTION_REQUESTED = '\u26a1 Action Requested';

export function ChainTable({ model }: { model: AdviceChainModel }) {
  if (!model.paths.length) return null;

  const paths = orderedPaths(model.paths);

  return (
    <div className="chain-concept-stack" aria-label="Food route panel concepts">
      {PANEL_CONCEPTS.map(concept => (
        <section className={`chain-concept-panel chain-concept--${concept.key}`} key={concept.key}>
          <header className="chain-concept-heading">
            <div>
              <span className="chain-concept-kicker">{concept.kicker}</span>
              <h3>{concept.name}</h3>
            </div>
            <p>{concept.description}</p>
          </header>
          {renderConcept(concept.key, paths)}
        </section>
      ))}
    </div>
  );
}

function JobCardsConcept({ paths }: { paths: AdviceChainPath[] }) {
  return (
    <div className="job-card-grid">
      {paths.map(path => {
        const route = routeKind(path);
        const status = routeStatus(path);

        return (
          <article className={`job-card job-card--${route} chain-route--${route}`} key={path.name}>
            <header className="job-card-header">
              <SemanticIconCue className="chain-route-icon" icon={routeIcon(route)} size="sm" />
              <div>
                <h4>{routeName(path.name)}</h4>
                <span className={`chain-status-badge chain-fact--${classFor(status)}`}>
                  <StateText status={status} />
                </span>
              </div>
            </header>
            <ul className="job-bullet-list">
              {routeFacts(path).slice(0, 4).map(fact => (
                <li className={`job-bullet chain-fact--${classFor(fact.status)}`} key={fact.key}>
                  <SemanticIconCue className="chain-fact-icon" icon={iconForField(fact.iconKey)} size="xs" />
                  <span className="chain-fact-label">{fact.label}</span>
                  <span className="chain-fact-value">
                    <StateText status={fact.status} text={fact.value} />
                  </span>
                </li>
              ))}
            </ul>
          </article>
        );
      })}
    </div>
  );
}

function ConsoleConcept({ paths }: { paths: AdviceChainPath[] }) {
  return (
    <div className="console-rack">
      {paths.map(path => {
        const route = routeKind(path);
        const status = routeStatus(path);

        return (
          <article className={`console-module console-module--${route} chain-route--${route}`} key={path.name}>
            <span className="console-state-rail" aria-hidden />
            <div className="console-module-body">
              <header className="console-module-header">
                <span className="console-callsign">{routeCode(route)}-01</span>
                <SemanticIconCue className="chain-route-icon" icon={routeIcon(route)} size="sm" />
                <div>
                  <h4>{routeName(path.name)}</h4>
                  <span className={`chain-status-badge chain-fact--${classFor(status)}`}>
                    <StateText status={status} />
                  </span>
                </div>
              </header>
              <div className="console-readouts">
                {routeFacts(path).slice(0, 4).map(fact => (
                  <div className={`console-readout chain-fact--${classFor(fact.status)}`} key={fact.key}>
                    <SemanticIconCue className="chain-fact-icon" icon={iconForField(fact.iconKey)} size="xs" />
                    <span className="chain-fact-label">{fact.label}</span>
                    <span className="chain-fact-value">
                      <StateText status={fact.status} text={fact.value} />
                    </span>
                  </div>
                ))}
              </div>
            </div>
          </article>
        );
      })}
    </div>
  );
}

function SubwayConcept({ paths }: { paths: AdviceChainPath[] }) {
  return (
    <div className="subway-map">
      {paths.map(path => {
        const route = routeKind(path);
        const status = routeStatus(path);

        return (
          <article className={`subway-route subway-route--${route} chain-route--${route}`} key={path.name}>
            <header className="subway-route-title">
              <SemanticIconCue className="chain-route-icon" icon={routeIcon(route)} size="sm" />
              <div>
                <h4>{routeName(path.name)}</h4>
                <span className={`chain-status-badge chain-fact--${classFor(status)}`}>
                  <StateText status={status} />
                </span>
              </div>
            </header>
            <ol className="subway-stops">
              {routeFacts(path).slice(0, 5).map(fact => (
                <li className={`subway-stop chain-fact--${classFor(fact.status)}`} key={fact.key}>
                  <span className="subway-stop-dot">
                    <SemanticIconCue className="chain-fact-icon" icon={iconForField(fact.iconKey)} size="xs" />
                  </span>
                  <span className="chain-fact-label">{fact.label}</span>
                  <span className="chain-fact-value">
                    <StateText status={fact.status} text={fact.value} />
                  </span>
                </li>
              ))}
            </ol>
          </article>
        );
      })}
    </div>
  );
}

function QuartermasterConcept({ paths }: { paths: AdviceChainPath[] }) {
  return (
    <div className="quartermaster-board">
      {paths.map(path => {
        const route = routeKind(path);
        const status = routeStatus(path);

        return (
          <article className={`quartermaster-ticket quartermaster-ticket--${route} chain-route--${route}`} key={path.name}>
            <span className="ticket-pin" aria-hidden />
            <header className="ticket-header">
              <SemanticIconCue className="chain-route-icon" icon={routeIcon(route)} size="sm" />
              <div>
                <span>{routeCode(route)} order</span>
                <h4>{routeName(path.name)}</h4>
              </div>
            </header>
            <div className={`ticket-stamp chain-fact--${classFor(status)}`}>
              <StateText status={status} />
            </div>
            <ul className="ticket-lines">
              {routeFacts(path).slice(0, 4).map(fact => (
                <li className={`ticket-line chain-fact--${classFor(fact.status)}`} key={fact.key}>
                  <SemanticIconCue className="chain-fact-icon" icon={iconForField(fact.iconKey)} size="xs" />
                  <span className="chain-fact-label">{fact.label}</span>
                  <span className="chain-fact-value">
                    <StateText status={fact.status} text={fact.value} />
                  </span>
                </li>
              ))}
            </ul>
          </article>
        );
      })}
    </div>
  );
}

function TacticalConcept({ paths }: { paths: AdviceChainPath[] }) {
  return (
    <div className="tactical-grid">
      {paths.map(path => {
        const route = routeKind(path);
        const status = routeStatus(path);
        const primary = primaryFact(path);

        return (
          <article className={`tactical-card tactical-card--${route} chain-route--${route}`} key={path.name}>
            <header className="tactical-header">
              <span className="tactical-index">{routeCode(route)}</span>
              <SemanticIconCue className="chain-route-icon" icon={routeIcon(route)} size="sm" />
            </header>
            <h4>{routeName(path.name)}</h4>
            <div className={`tactical-primary chain-fact--${classFor(status)}`}>
              <StateText status={status} />
            </div>
            <div className="tactical-chips">
              {routeFacts(path).slice(0, 4).map(fact => (
                <span className={`tactical-chip chain-fact--${classFor(fact.status)}`} key={fact.key}>
                  <SemanticIconCue className="chain-fact-icon" icon={iconForField(fact.iconKey)} size="xs" />
                  <span className="chain-fact-label">{fact.label}</span>
                </span>
              ))}
            </div>
            {primary ? (
              <footer className={`tactical-command chain-fact--${classFor(primary.status)}`}>
                <span>{primary.label}</span>
                <strong>
                  <StateText status={primary.status} text={primary.value} />
                </strong>
              </footer>
            ) : null}
          </article>
        );
      })}
    </div>
  );
}

const PANEL_CONCEPTS = [
  {
    key: 'jobs',
    kicker: 'Option 01',
    name: 'RimWorld Job Cards',
    description: 'Chunky work orders with short icon bullets.',
  },
  {
    key: 'console',
    kicker: 'Option 02',
    name: 'Colony Supply Console',
    description: 'A darker terminal rack with live readouts.',
  },
  {
    key: 'subway',
    kicker: 'Option 03',
    name: 'Food Chain Subway Map',
    description: 'Routes and stops show the supply chain flow.',
  },
  {
    key: 'quartermaster',
    kicker: 'Option 04',
    name: 'Quartermaster Board',
    description: 'Pinned colony orders with stamped state.',
  },
  {
    key: 'tactical',
    kicker: 'Option 05',
    name: 'Tactical Cards',
    description: 'Strategy-game command cards with loud action bars.',
  },
] as const;

type PanelConceptKey = (typeof PANEL_CONCEPTS)[number]['key'];

type RouteFact = {
  iconKey: string;
  key: string;
  label: string;
  status: AdviceChainStepStatus;
  value: string;
};

type RouteKind = 'forage' | 'grow' | 'hunt';

function renderConcept(key: PanelConceptKey, paths: AdviceChainPath[]) {
  if (key === 'jobs') return <JobCardsConcept paths={paths} />;
  if (key === 'console') return <ConsoleConcept paths={paths} />;
  if (key === 'subway') return <SubwayConcept paths={paths} />;
  if (key === 'quartermaster') return <QuartermasterConcept paths={paths} />;
  return <TacticalConcept paths={paths} />;
}

function StateText({ status, text }: { status: AdviceChainStepStatus; text?: string }) {
  const display = text ?? formatStatus(status);
  if (status === 'action') return <strong>{display}</strong>;
  return <>{display}</>;
}

function classFor(status: AdviceChainStepStatus): AdviceChainStepStatus {
  return status;
}

function formatStatus(status: AdviceChainStepStatus): string {
  if (status === 'have') return 'current good';
  if (status === 'action') return ACTION_REQUESTED;
  if (status === 'available') return 'future';
  if (status === 'blocked') return 'blocked';
  return 'trigger';
}

function orderedPaths(paths: AdviceChainPath[]): AdviceChainPath[] {
  const order: Record<RouteKind, number> = { grow: 0, hunt: 1, forage: 2 };
  return [...paths].sort((left, right) => order[routeKind(left)] - order[routeKind(right)]);
}

function routeCode(route: RouteKind): string {
  if (route === 'hunt') return 'HNT';
  if (route === 'forage') return 'FRG';
  return 'GRO';
}

function routeName(name: string): string {
  return name.replace(/\s+path$/i, '');
}

function routeKind(path: AdviceChainPath): RouteKind {
  const name = path.name.toLowerCase();
  if (name.includes('hunt')) return 'hunt';
  if (name.includes('forage')) return 'forage';
  return 'grow';
}

function routeIcon(route: RouteKind) {
  if (route === 'hunt') return iconForField('mark_hunt');
  if (route === 'forage') return iconForField('wild_harvest_clusters');
  return iconForField('crop_zone_summaries');
}

function routeStatus(path: AdviceChainPath): AdviceChainStepStatus {
  const steps = routeSteps(path);

  if (steps.some(step => step.status === 'action')) return 'action';
  if (steps.some(step => step.status === 'blocked')) return 'blocked';
  if (steps.some(step => step.status === 'available')) return 'available';
  if (steps.every(step => step.status === 'have')) return 'have';
  return 'available';
}

function routeSteps(path: AdviceChainPath): AdviceChainStep[] {
  return path.steps.filter(step => !isSharedStep(step));
}

function isSharedStep(step: AdviceChainStep): boolean {
  const key = step.key.toLowerCase();
  return key.endsWith('.trigger') || key.endsWith('.meals') || step.label === 'Food buffer' || step.label === 'Meals';
}

function routeFacts(path: AdviceChainPath): RouteFact[] {
  const route = routeKind(path);
  if (route === 'hunt') return huntFacts(path);
  if (route === 'forage') return forageFacts(path);
  return growFacts(path);
}

function growFacts(path: AdviceChainPath): RouteFact[] {
  const zone = findStep(path, 'grow.zone');
  const harvest = findStep(path, 'grow.harvest');
  const cook = findStep(path, 'grow.cook');
  const store = findStep(path, 'grow.store');

  return compactFacts([
    stepFact('zone', 'Zone', zone, 'crop_zone_summaries', stepDetail(zone)),
    stepFact('crops', 'Crops', harvest, 'crop_breakdown', stepDetail(harvest)),
    stepFact('harvest', 'Harvest', harvest, 'mark_harvest', statusPhrase(harvest)),
    stepFact('cook', 'Cook', cook, 'production_bill', detailOrStatus(cook)),
    stepFact('store', 'Store', store, 'storage', statusPhrase(store)),
  ]);
}

function huntFacts(path: AdviceChainPath): RouteFact[] {
  const hunt = findStep(path, 'hunt.hunt');
  const butcher = findStep(path, 'hunt.butcher');
  const cook = findStep(path, 'hunt.cook');
  const store = findStep(path, 'hunt.store');

  return compactFacts([
    stepFact('target', 'Target', hunt, 'wild_animal_count', stepDetail(hunt)),
    stepFact('hunt', 'Hunt', hunt, 'mark_hunt', statusPhrase(hunt)),
    stepFact('butcher', 'Butcher', butcher, 'kitchen_and_butchery', stepDetail(butcher)),
    stepFact('cook', 'Cook', cook, 'production_bill', detailOrStatus(cook)),
    stepFact('store', 'Store', store, 'storage', statusPhrase(store)),
  ]);
}

function forageFacts(path: AdviceChainPath): RouteFact[] {
  const forage = findStep(path, 'forage.harvest');
  const cook = findStep(path, 'forage.cook');
  const store = findStep(path, 'forage.store');

  return compactFacts([
    stepFact('plants', 'Plants', forage, 'wild_harvest_clusters', stepDetail(forage)),
    stepFact('harvest', 'Harvest', forage, 'mark_harvest', statusPhrase(forage)),
    stepFact('cook', 'Cook', cook, 'production_bill', detailOrStatus(cook)),
    stepFact('store', 'Store', store, 'storage', statusPhrase(store)),
  ]);
}

function stepFact(
  key: string,
  label: string,
  step: AdviceChainStep | undefined,
  iconKey: string,
  value: string,
): RouteFact | null {
  if (!step) return null;
  return { iconKey, key, label, status: step.status, value };
}

function compactFacts(facts: Array<RouteFact | null>): RouteFact[] {
  return facts.filter((fact): fact is RouteFact => fact !== null).slice(0, 5);
}

function primaryFact(path: AdviceChainPath): RouteFact | undefined {
  const facts = routeFacts(path);
  return facts.find(fact => fact.status === 'action')
    ?? facts.find(fact => fact.status === 'blocked')
    ?? facts.find(fact => fact.status === 'available')
    ?? facts[0];
}

function findStep(path: AdviceChainPath, key: string): AdviceChainStep | undefined {
  return path.steps.find(step => step.key === key);
}

function detailOrStatus(step: AdviceChainStep | undefined): string {
  if (!step) return '-';
  if (step.status === 'action') return ACTION_REQUESTED;
  if (step.status === 'blocked') return cleanBlockedDetail(step.detail);
  return step.detail;
}

function stepDetail(step: AdviceChainStep | undefined): string {
  if (!step) return '-';
  return step.status === 'blocked' ? cleanBlockedDetail(step.detail) : step.detail;
}

function statusPhrase(step: AdviceChainStep | undefined): string {
  if (!step) return '-';
  if (step.status === 'action') return ACTION_REQUESTED;
  if (step.status === 'blocked') return cleanBlockedDetail(step.detail);
  if (step.status === 'available') return 'available';
  if (step.status === 'have') return 'covered';
  return 'pressure';
}

function cleanBlockedDetail(detail: string): string {
  return detail
    .replace(/^no food /i, 'no ')
    .replace(/^no /i, 'no ')
    .replace(/ visible$/i, '');
}
