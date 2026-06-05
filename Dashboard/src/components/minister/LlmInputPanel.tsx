import { useId, useState, type ReactNode } from 'react';
import type { SemanticIconSpec } from '../../dashboard/semanticIcons';
import { SemanticLabel } from '../shared/SemanticIcon';

export function LlmInputPanel({
  children,
  defaultOpen = false,
  icon,
  meta,
  title,
}: {
  children: ReactNode;
  defaultOpen?: boolean;
  icon: SemanticIconSpec | undefined;
  meta?: string;
  title: string;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const buttonId = useId();
  const panelId = useId();

  return (
    <section className={`llm-input-panel ${open ? 'open' : ''}`}>
      <h3>
        <button
          id={buttonId}
          type="button"
          aria-controls={panelId}
          aria-expanded={open}
          onClick={() => setOpen(value => !value)}
        >
          <SemanticLabel icon={icon}><span>{title}</span></SemanticLabel>
          <small>{meta ?? (open ? 'open' : 'closed')}</small>
        </button>
      </h3>
      {open && (
        <div
          id={panelId}
          className="llm-input-panel-body"
          role="region"
          aria-labelledby={buttonId}
        >
          {children}
        </div>
      )}
    </section>
  );
}
