import type { LogEntry, LogEntryKind, LogStep } from '../../dashboard/selectors';
import { EmptyState } from '../shared/EmptyState';

export function SidebarLog({
  clearedAt,
  entries,
  expandedIds,
  onClear,
  onExpandedIdsChange,
}: {
  clearedAt: string | null;
  entries: LogEntry[];
  expandedIds: Set<string>;
  onClear: () => void;
  onExpandedIdsChange: (nextExpandedIds: Set<string>) => void;
}) {
  const hasEntries = entries.length > 0;

  const clearLog = () => {
    onExpandedIdsChange(new Set());
    onClear();
  };

  const toggleEntry = (entryId: string) => {
    const next = new Set(expandedIds);
    if (next.has(entryId)) {
      next.delete(entryId);
    } else {
      next.add(entryId);
    }
    onExpandedIdsChange(next);
  };

  return (
    <section className="sidebar-log" aria-label="Important event log">
      <header className="sidebar-log-header">
        <div>
          <span className="eyebrow">Log</span>
          <h2>{hasEntries ? `${entries.length} ${entries.length === 1 ? 'event' : 'events'}` : 'No events'}</h2>
        </div>
        <button
          aria-label="Clear visible LOG events"
          className="log-clear-button"
          disabled={!hasEntries}
          title="Clear visible LOG events for this browser session"
          type="button"
          onClick={clearLog}
        >
          Clear
        </button>
      </header>
      {!hasEntries && (
        <EmptyState code={clearedAt ? 'LOG CLEARED' : 'LOG'}>
          {clearedAt ? 'New important events will appear here.' : 'No important events yet this session.'}
        </EmptyState>
      )}
      {hasEntries && (
      <div className="sidebar-log-list">
        {entries.map(entry => {
          const steps = entry.steps ?? [];
          const expandable = steps.length > 0;
          const expanded = expandedIds.has(entry.id);
          const stepsId = `log-steps-${safeDomId(entry.id)}`;

          return (
            <article className={`log-card ${entry.tone}`} key={entry.id} title={expandable ? undefined : entry.tooltip}>
              <div className="log-card-top">
                {expandable ? (
                  <button
                    aria-controls={stepsId}
                    aria-expanded={expanded}
                    className="log-card-main log-card-toggle"
                    title={entry.tooltip}
                    type="button"
                    onClick={() => toggleEntry(entry.id)}
                  >
                    <LogCardHeading entry={entry} />
                    <span className={`log-card-disclosure ${expanded ? 'open' : ''}`} aria-hidden="true">›</span>
                  </button>
                ) : (
                  <div className="log-card-main">
                    <LogCardHeading entry={entry} />
                  </div>
                )}
                {entry.link && <LogLinkButton className="log-card-link" link={entry.link} />}
              </div>
              {entry.chips.length > 0 && (
                <div className="log-card-chips" aria-label="Event details">
                  {entry.chips.map(chip => (
                    <span className="log-chip" key={`${entry.id}:${chip.tooltip}`} title={chip.tooltip}>
                      <span aria-hidden="true">{chip.emoji}</span>
                      <span>{chip.label}</span>
                    </span>
                  ))}
                </div>
              )}
              {expandable && expanded && (
                <ol className="log-step-list" id={stepsId}>
                  {steps.map(step => <LogStepRow key={step.id} step={step} />)}
                </ol>
              )}
            </article>
          );
        })}
      </div>
      )}
    </section>
  );
}

function LogCardHeading({ entry }: { entry: LogEntry }) {
  return (
    <>
      <span className="log-card-icon" aria-hidden="true">{entry.icon}</span>
      <div className="log-card-copy">
        <h3>{entry.title}</h3>
        <div className="log-card-meta">
          <time dateTime={entry.at}>{formatLogTime(entry.at)}</time>
          <span>{kindLabel(entry.kind)}</span>
        </div>
      </div>
    </>
  );
}

function LogLinkButton({ className, link }: { className: string; link: NonNullable<LogEntry['link']> }) {
  return (
    <a
      aria-label={link.ariaLabel}
      className={className}
      href={link.href}
      title={link.tooltip}
    >
      <span aria-hidden="true">↗</span>
    </a>
  );
}

function LogStepRow({ step }: { step: LogStep }) {
  const showStatus = step.status !== 'completed';
  return (
    <li className={`log-step ${step.status}`} title={fullStepTooltip(step)}>
      <div className="log-step-row">
        <span className={`log-step-dot ${step.tone}`} aria-hidden="true" />
        <div className="log-step-copy">
          <div className="log-step-title">
            <strong>{stepTitle(step)}</strong>
            <span className="log-step-title-actions">
              {step.link && <LogLinkButton className="log-step-link" link={step.link} />}
              <span className="log-step-time">{formatStepDuration(step.durationMs)}</span>
            </span>
          </div>
          {showStatus && <span className="log-step-status">{formatStepStatus(step.status)}</span>}
          <ul className="log-step-facts">
            {step.optionsCount !== null && (
              <li>generated {step.optionsCount.toLocaleString()} {step.optionsCount === 1 ? 'option' : 'options'}</li>
            )}
            <NamedFact count={step.adviceCount} items={step.advice} label="advice" />
            <NamedFact count={step.flagCount} items={step.flags} label="flags" />
          </ul>
        </div>
      </div>
      {step.children.length > 0 && (
        <ol className="log-step-list nested">
          {step.children.map(child => <LogStepRow key={child.id} step={child} />)}
        </ol>
      )}
    </li>
  );
}

function NamedFact({
  count,
  items,
  label,
}: {
  count: number | null;
  items: { label: string; tooltip: string }[];
  label: string;
}) {
  const effectiveCount = count ?? items.length;
  if (effectiveCount === 0 && items.length === 0) return null;

  return (
    <li title={namedFactTooltip(label, items, effectiveCount)}>
      {label}: {namedFactSummary(items, effectiveCount, label)}
    </li>
  );
}

function kindLabel(kind: LogEntryKind): string {
  switch (kind) {
    case 'action_applied':
      return 'Apply';
    case 'cabinet_run':
      return 'Cabinet';
    case 'minister_run':
      return 'Run';
    case 'escalation':
      return 'LLM';
    case 'critical_advice':
      return 'Critical';
  }
}

function stepTitle(step: LogStep): string {
  if (step.kind === 'solver') return stripSourceSuffix(step.label);
  if (step.ruleNames.length > 0) return friendlyRuleName(step.ruleNames[0]);
  return step.label;
}

function stripSourceSuffix(label: string): string {
  return label.replace(/\s+\(from [^)]+\)$/i, '').trim();
}

function namedFactSummary(items: { label: string }[], count: number, label: string): string {
  const countLabel = namedFactCountLabel(label, count);
  if (items.length === 0) return `${count.toLocaleString()} ${countLabel}`;

  const names = items.slice(0, 2).map(item => item.label);
  const continuation = count > names.length ? ', ...' : '';
  return `${names.join(', ')}${continuation} (${count.toLocaleString()} ${countLabel})`;
}

function namedFactCountLabel(label: string, count: number): string {
  if (label === 'flags') return count === 1 ? 'flag' : 'flags';
  return label;
}

function namedFactTooltip(label: string, items: { label: string; tooltip: string }[], count: number): string {
  if (items.length === 0) {
    return `${capitalize(label)}: ${count} ${namedFactCountLabel(label, count)}\n- Cabinet run snapshots expose only the count for this row.`;
  }

  const itemLines = items.map(item => `- ${item.tooltip}`);
  if (count > items.length) {
    itemLines.push(`- ${count - items.length} more not named in this dashboard snapshot`);
  }

  return `${capitalize(label)} (${count})\n${itemLines.join('\n')}`;
}

function fullStepTooltip(step: LogStep): string {
  return [
    step.ruleNames.length > 0 ? `Rules\n${step.ruleNames.map(ruleName => `- ${friendlyRuleName(ruleName)} (${ruleName})`).join('\n')}` : null,
    step.detail ? `Detail\n- ${step.detail}` : null,
  ].filter(Boolean).join('\n\n');
}

function friendlyRuleName(ruleName: string): string {
  return ruleName
    .replace(/_/g, ' ')
    .replace(/\b\w/g, letter => letter.toUpperCase());
}

function capitalize(value: string): string {
  return value.slice(0, 1).toUpperCase() + value.slice(1);
}

function formatLogTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const ageMs = Date.now() - date.getTime();
  if (ageMs >= 0 && ageMs < 90_000) return 'just now';
  if (ageMs >= 0 && ageMs < 60 * 60_000) return `${Math.round(ageMs / 60_000)}m ago`;

  const now = new Date();
  const sameDay =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();

  return sameDay
    ? date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : date.toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function formatStepDuration(durationMs: number | null): string {
  if (durationMs === null || !Number.isFinite(durationMs)) return 'running';
  if (durationMs < 1000) return `${Math.max(0, Math.round(durationMs))}ms`;
  const seconds = durationMs / 1000;
  return `${seconds.toFixed(seconds >= 10 ? 0 : 1)}s`;
}

function formatStepStatus(status: string): string {
  return status
    .replace(/_/g, ' ')
    .replace(/\b\w/g, letter => letter.toUpperCase());
}

function safeDomId(value: string): string {
  return value.replace(/[^a-zA-Z0-9_-]+/g, '-');
}
