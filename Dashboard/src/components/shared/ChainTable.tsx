import type { AdviceChainModel, AdviceChainPath, AdviceChainStep, AdviceChainStepStatus } from '../../types/advice';

export function ChainTable({ model }: { model: AdviceChainModel }) {
  if (!model.paths.length) return null;

  return (
    <div className="chain-route-grid" aria-label="Food route status">
      {model.paths.map(path => (
        <article className={`chain-route-card chain-cell--${classFor(routeStatus(path))}`} key={path.name}>
          <span>{formatStatus(routeStatus(path))}</span>
          <h3>{routeName(path.name)}</h3>
          <p>{routeSummary(path)}</p>
        </article>
      ))}
    </div>
  );
}

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

function routeStatus(path: AdviceChainPath): AdviceChainStepStatus {
  const steps = routeSteps(path);

  if (steps.some(step => step.status === 'action')) return 'action';
  if (steps.some(step => step.status === 'blocked')) return 'blocked';
  if (steps.some(step => step.status === 'available')) return 'available';
  if (steps.every(step => step.status === 'have')) return 'have';
  return 'available';
}

function routeSummary(path: AdviceChainPath): string {
  const steps = routeSteps(path);
  const actionSteps = steps.filter(step => step.status === 'action');
  const blockedSteps = steps.filter(step => step.status === 'blocked');
  const futureSteps = steps.filter(step => step.status === 'available' || step.status === 'trigger');

  if (actionSteps.length > 0) return `Action: ${labelList(actionSteps)}`;
  if (blockedSteps.length > 0) return `Blocked: ${labelList(blockedSteps)}`;
  if (futureSteps.length > 0) return `Future: ${labelList(futureSteps)}`;
  return 'Current state good';
}

function routeSteps(path: AdviceChainPath): AdviceChainStep[] {
  return path.steps.filter(step => !isSharedStep(step));
}

function isSharedStep(step: AdviceChainStep): boolean {
  const key = step.key.toLowerCase();
  return key.endsWith('.trigger') || key.endsWith('.meals') || step.label === 'Food buffer' || step.label === 'Meals';
}

function labelList(steps: AdviceChainStep[]): string {
  return steps
    .map(step => step.label)
    .filter((label, index, labels) => labels.indexOf(label) === index)
    .join(', ');
}
