import type { MayorAgenda } from '../../types/agenda';
import type { SystemHealth } from '../../types/system';
import type { ScopeConfig } from '../../dashboard/scopes';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { MetricCard } from '../shared/MetricCard';

export function MinisterRagView({
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
    <div className="minister-view rag-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2>📚 RAG</h2>
        <p>Guide retrieval and citations. Full per-minister retrieval traces are planned.</p>
      </header>

      <div className="metric-grid">
        <MetricCard label="📚 RAG" value={systemHealth?.rag.enabled ? 'enabled' : 'unknown'} />
        <MetricCard label="🧩 Chunks" value={systemHealth?.rag.chunk_count ?? 'n/a'} />
        <MetricCard label="🎯 Top K" value={systemHealth?.rag.top_k ?? 'n/a'} />
        <MetricCard label="🚧 Endpoint" value="/rag/latest" note="not exposed yet" tone="warn" />
      </div>

      {scope.key !== 'mayor' && (
        <EmptyState code="RAG TRACE NOT EXPOSED">
          {scope.label} retrieval query and guide chunks need a minister RAG endpoint.
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
          title={`${citation.cite_id} - ${citation.heading}`}
          meta={citation.source_path}
          defaultOpen
        >
          <p className="citation-snippet">{citation.snippet}</p>
        </DisclosureSection>
      ))}
    </div>
  );
}
