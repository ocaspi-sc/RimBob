import type { ReactNode } from 'react';

export function EmptyState({
  code,
  children,
}: {
  code: string;
  children: ReactNode;
}) {
  return (
    <div className="empty-state">
      <span>{code}</span>
      <p>{children}</p>
    </div>
  );
}
