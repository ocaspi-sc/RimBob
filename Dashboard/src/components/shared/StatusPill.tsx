import type { ReactNode } from 'react';

export type PillTone = 'ok' | 'warn' | 'error' | 'idle' | 'info';

export function StatusPill({
  children,
  tone = 'idle',
  title,
}: {
  children: ReactNode;
  tone?: PillTone;
  title?: string;
}) {
  return (
    <span className={`status-pill ${tone}`} title={title}>
      <span className="status-dot" title={title} aria-hidden />
      {children}
    </span>
  );
}
