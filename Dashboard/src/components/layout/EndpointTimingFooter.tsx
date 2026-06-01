import { useId, useRef, useState } from 'react';
import { SlowEndpointQueryMs, type EndpointQueryTiming } from '../../api/requestTelemetry';
import { useEndpointQueryTimings } from '../../hooks/useEndpointQueryTimings';

interface EndpointTimingFooterProps {
  pageKey: string;
}

interface EndpointTimingSummary {
  key: string;
  method: string;
  url: string;
  state: EndpointQueryTiming['state'];
  statusCode: number | null;
  latestDurationMs: number;
  maxDurationMs: number;
  completedAt: number;
  count: number;
  error: string | null;
}

export function EndpointTimingFooter({ pageKey }: EndpointTimingFooterProps) {
  const pageStartedAt = usePageStartedAt(pageKey);
  const [open, setOpen] = useState(false);
  const buttonId = useId();
  const panelId = useId();
  const timings = useEndpointQueryTimings();
  const summaries = summarizeEndpointTimings(timings, pageStartedAt);

  return (
    <footer className="endpoint-timing-footer" aria-label="Endpoints used by this dashboard page">
      <button
        id={buttonId}
        type="button"
        className="endpoint-timing-footer-header"
        aria-controls={panelId}
        aria-expanded={open}
        onClick={() => setOpen(value => !value)}
      >
        <span className="eyebrow">Endpoints used</span>
        <span className="endpoint-timing-header-meta">
          <strong>{summaries.length}</strong>
          <small>{open ? 'open' : 'closed'}</small>
        </span>
      </button>
      {open && (
        <div id={panelId} role="region" aria-labelledby={buttonId}>
          {summaries.length === 0 ? (
            <div className="endpoint-timing-empty">No endpoint queries recorded for this page yet.</div>
          ) : (
            <div className="endpoint-timing-list">
              {summaries.map(summary => {
                const slow = summary.maxDurationMs >= SlowEndpointQueryMs;
                return (
                  <div className={`endpoint-timing-row ${slow ? 'slow' : ''}`} key={summary.key}>
                    <span className="endpoint-timing-method">{summary.method}</span>
                    <code>{summary.url}</code>
                    <span className={`endpoint-timing-state ${summary.state}`}>
                      {summary.statusCode ?? summary.state}
                    </span>
                    <span className="endpoint-timing-ms">{formatMs(summary.latestDurationMs)}</span>
                    <small>{summary.count > 1 ? `${summary.count}x` : formatCompletedAt(summary.completedAt)}</small>
                    {summary.error && <small className="endpoint-timing-error">{summary.error}</small>}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}
    </footer>
  );
}

function usePageStartedAt(pageKey: string): number {
  const state = useRef<{ pageKey: string; startedAt: number }>({
    pageKey,
    startedAt: Date.now(),
  });

  if (state.current.pageKey !== pageKey) {
    state.current = {
      pageKey,
      startedAt: Date.now(),
    };
  }

  return state.current.startedAt;
}

function summarizeEndpointTimings(
  timings: EndpointQueryTiming[],
  pageStartedAt: number,
): EndpointTimingSummary[] {
  const summaries = new Map<string, EndpointTimingSummary>();

  for (const timing of timings) {
    if (timing.state === 'aborted' || timing.completedAt < pageStartedAt) {
      continue;
    }

    const key = `${timing.method} ${timing.url}`;
    const existing = summaries.get(key);
    if (!existing) {
      summaries.set(key, {
        key,
        method: timing.method,
        url: timing.url,
        state: timing.state,
        statusCode: timing.statusCode,
        latestDurationMs: timing.durationMs,
        maxDurationMs: timing.durationMs,
        completedAt: timing.completedAt,
        count: 1,
        error: timing.error,
      });
      continue;
    }

    existing.count += 1;
    existing.maxDurationMs = Math.max(existing.maxDurationMs, timing.durationMs);
    if (timing.completedAt > existing.completedAt) {
      existing.state = timing.state;
      existing.statusCode = timing.statusCode;
      existing.latestDurationMs = timing.durationMs;
      existing.completedAt = timing.completedAt;
      existing.error = timing.error;
    }
  }

  return [...summaries.values()]
    .sort((left, right) => right.completedAt - left.completedAt);
}

function formatMs(durationMs: number): string {
  if (durationMs < 1000) return `${Math.round(durationMs)} ms`;
  return `${(durationMs / 1000).toFixed(2)} s`;
}

function formatCompletedAt(timestamp: number): string {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return 'just now';
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}
