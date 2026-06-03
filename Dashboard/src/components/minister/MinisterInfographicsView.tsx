import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForView } from '../../dashboard/semanticIcons';
import type { AdviceChainModel } from '../../types/advice';
import { ChainTable } from '../shared/ChainTable';
import { EmptyState } from '../shared/EmptyState';
import { SemanticLabel } from '../shared/SemanticIcon';

export function MinisterInfographicsView({
  chain,
  scope,
}: {
  chain: AdviceChainModel | null;
  scope: ScopeConfig;
}) {
  if (scope.status !== 'live') {
    return <EmptyState code="INFOGRAPHICS NOT WIRED">{scope.displayLabel} is planned and has no infographic data yet.</EmptyState>;
  }

  if (!chain?.paths.length) {
    return <EmptyState code="NO INFOGRAPHICS">{scope.displayLabel} has not published infographic data in this session.</EmptyState>;
  }

  return (
    <div className="minister-view infographics-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.displayLabel}</span>
        <h2><SemanticLabel icon={iconForView('infographics')}><span>Infographics</span></SemanticLabel></h2>
        <p>Visual minister evidence from the current snapshot.</p>
      </header>
      <section className="advice-chain">
        <div className="advice-chain-heading">
          <span className="eyebrow">Food Chain - Work Order Flow</span>
        </div>
        <ChainTable model={chain} />
      </section>
    </div>
  );
}
