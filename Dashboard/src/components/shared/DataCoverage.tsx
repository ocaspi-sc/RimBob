import type { CoverageState, EndpointCoverage } from '../../types/system';

const labelByState: Record<string, string> = {
  available: 'Available',
  missing: 'Missing',
  failed: 'Failed',
  unsupported: 'Unsupported',
  stale: 'Stale',
  partial: 'Partial',
  not_exposed_yet: 'Not exposed',
};

export function CoverageBadge({ state }: { state: CoverageState | string }) {
  const label = labelByState[state] ?? state;
  return <span className={`coverage-badge ${state}`}>{label}</span>;
}

export function CoverageTable({ rows }: { rows: EndpointCoverage[] }) {
  if (rows.length === 0) {
    return <div className="muted-row">No coverage rows reported.</div>;
  }

  return (
    <div className="dense-table coverage-table">
      <div className="dense-row header">
        <span>Surface</span>
        <span>Status</span>
        <span>Notes</span>
      </div>
      {rows.map(row => (
        <div className="dense-row" key={row.endpoint}>
          <code>{row.endpoint}</code>
          <CoverageBadge state={row.state} />
          <span>{row.note}</span>
        </div>
      ))}
    </div>
  );
}
