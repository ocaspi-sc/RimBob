import { useId, useState, type ReactNode } from 'react';

export function DisclosureSection({
  children,
  defaultOpen = false,
  meta,
  title,
}: {
  children: ReactNode;
  defaultOpen?: boolean;
  meta?: string;
  title: ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const buttonId = useId();
  const panelId = useId();

  return (
    <section className={`disclosure-card ${open ? 'open' : ''}`}>
      <h3>
        <button
          id={buttonId}
          type="button"
          aria-controls={panelId}
          aria-expanded={open}
          onClick={() => setOpen(value => !value)}
        >
          <span>{title}</span>
          <small>{meta ?? (open ? 'open' : 'closed')}</small>
        </button>
      </h3>
      {open && (
        <div
          id={panelId}
          className="disclosure-panel"
          role="region"
          aria-labelledby={buttonId}
        >
          {children}
        </div>
      )}
    </section>
  );
}
