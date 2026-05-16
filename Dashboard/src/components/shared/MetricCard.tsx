import type { ReactNode } from 'react';

export function MetricCard({
  label,
  value,
  note,
  tone = 'neutral',
}: {
  label: ReactNode;
  value: ReactNode;
  note?: string;
  tone?: 'neutral' | 'ok' | 'warn' | 'error';
}) {
  return (
    <div className={`metric-card ${tone}`}>
      <div className="metric-label">{label}</div>
      <strong>{value}</strong>
      {note && <small>{note}</small>}
    </div>
  );
}
