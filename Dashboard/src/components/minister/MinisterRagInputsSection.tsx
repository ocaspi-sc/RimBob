import type { MayorAgenda } from '../../types/agenda';
import type { SystemHealth } from '../../types/system';
import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForField, iconForView } from '../../dashboard/semanticIcons';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { MetricCard } from '../shared/MetricCard';
import { SemanticLabel } from '../shared/SemanticIcon';
import { LlmInputPanel } from './LlmInputPanel';

export function MinisterRagInputsSection({
  agenda,
  scope,
  systemHealth,
}: {
  agenda: MayorAgenda | null;
  scope: ScopeConfig;
  systemHealth: SystemHealth | null;
}) {
  const citations = scope.key === 'mayor' ? agenda?.guide_citations ?? [] : [];

  return (
    <LlmInputPanel
      icon={iconForView('rag')}
      title="RAG"
      meta={`${systemHealth?.rag.chunk_count ?? 'n/a'} chunks`}
    >
      <header className="section-heading">
        <p className="notes-copy">Guide retrieval and citations. Full per-minister retrieval traces are planned.</p>
      </header>

      <div className="metric-grid">
        <MetricCard label={<SemanticLabel icon={iconForField('rag')}><span>RAG</span></SemanticLabel>} value={systemHealth?.rag.enabled ? 'enabled' : 'unknown'} />
        <MetricCard label={<SemanticLabel icon={iconForField('chunks')}><span>Chunks</span></SemanticLabel>} value={systemHealth?.rag.chunk_count ?? 'n/a'} />
        <MetricCard label={<SemanticLabel icon={iconForField('top_k')}><span>Top K</span></SemanticLabel>} value={systemHealth?.rag.top_k ?? 'n/a'} />
        <MetricCard label={<SemanticLabel icon={iconForField('endpoint')}><span>Endpoint</span></SemanticLabel>} value="/rag/latest" note="not exposed yet" tone="warn" />
      </div>

      {scope.key !== 'mayor' && (
        <EmptyState code="RAG TRACE NOT EXPOSED">
          {scope.displayLabel} retrieval query and guide chunks need a minister RAG endpoint.
        </EmptyState>
      )}

      {scope.key === 'mayor' && citations.length === 0 && (
        <EmptyState code="NO CITATIONS">
          No server-stamped Mayor guide citations are present on the current agenda.
        </EmptyState>
      )}

      {citations.map(citation => (
        <DisclosureSection
          key={citation.cite_id}
          title={<SemanticLabel icon={iconForField('guide_citations')}><span>{`${citation.cite_id} - ${citation.heading}`}</span></SemanticLabel>}
          meta={citation.source_path}
          defaultOpen
        >
          <p className="citation-snippet">{citation.snippet}</p>
        </DisclosureSection>
      ))}
    </LlmInputPanel>
  );
}
