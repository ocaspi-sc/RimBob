import type { AdviceChainModel, AdviceChainPath, AdviceChainStep, AdviceChainStepStatus } from '../../types/advice';
import { iconForField } from '../../dashboard/semanticIcons';
import { SemanticIconCue } from './SemanticIcon';

export function ChainTable({ model }: { model: AdviceChainModel }) {
  if (!model.paths.length) return null;

  return (
    <div className="chain-route-grid" aria-label="Food route status">
      {model.paths.map(path => {
        const route = routeKind(path);
        const status = routeStatus(path);
        return (
          <article className={`chain-route-card chain-route--${route}`} key={path.name}>
            <header>
              <SemanticIconCue className="chain-route-icon" icon={routeIcon(route)} size="sm" />
              <div>
                <h3>{routeName(path.name)}</h3>
                <span className={`chain-route-status chain-fact--${classFor(status)}`}>{formatStatus(status)}</span>
              </div>
            </header>
            <ul className="chain-route-facts">
              {routeFacts(path).map(fact => (
                <li className={`chain-route-fact chain-fact--${classFor(fact.status)}`} key={fact.key}>
                  <SemanticIconCue className="chain-fact-icon" icon={iconForField(fact.iconKey)} size="xs" />
                  <span className="chain-fact-label">{fact.label}</span>
                  <span className="chain-fact-value">{fact.value}</span>
                </li>
              ))}
            </ul>
          </article>
        );
      })}
    </div>
  );
}

type RouteFact = {
  iconKey: string;
  key: string;
  label: string;
  status: AdviceChainStepStatus;
  value: string;
};

type RouteKind = 'forage' | 'grow' | 'hunt';

function classFor(status: AdviceChainStepStatus): AdviceChainStepStatus {
  return status;
}

function formatStatus(status: AdviceChainStepStatus): string {
  if (status === 'have') return 'current good';
  if (status === 'action') return 'action emitted';
  if (status === 'available') return 'future';
  if (status === 'blocked') return 'blocked';
  return 'trigger';
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

function findStep(path: AdviceChainPath, key: string): AdviceChainStep | undefined {
  return path.steps.find(step => step.key === key);
}

function detailOrStatus(step: AdviceChainStep | undefined): string {
  if (!step) return '-';
  if (step.status === 'action') return 'action requested';
  if (step.status === 'blocked') return cleanBlockedDetail(step.detail);
  return step.detail;
}

function stepDetail(step: AdviceChainStep | undefined): string {
  if (!step) return '-';
  return step.status === 'blocked' ? cleanBlockedDetail(step.detail) : step.detail;
}

function statusPhrase(step: AdviceChainStep | undefined): string {
  if (!step) return '-';
  if (step.status === 'action') return 'action requested';
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
