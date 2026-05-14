import type { ReactNode } from 'react';

export type PillTone = 'ok' | 'warn' | 'error' | 'idle' | 'info';

export function StatusPill({
  children,
  tone = 'idle',
}: {
  children: ReactNode;
  tone?: PillTone;
}) {
  return (
    <span className={`status-pill ${tone}`}>
      <span className="status-dot" aria-hidden />
      {children}
    </span>
  );
}
