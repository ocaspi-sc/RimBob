import type { ReactNode } from 'react';

export function MetricCard({
  label,
  value,
  note,
  tone = 'neutral',
}: {
  label: string;
  value: ReactNode;
  note?: string;
  tone?: 'neutral' | 'ok' | 'warn' | 'error';
}) {
  return (
    <div className={`metric-card ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
      {note && <small>{note}</small>}
    </div>
  );
}
