import { useMemo, useState } from 'react';
import { fetchDevBlogHistory } from '../../api/devBlog';
import type {
  DevBlogCommitSummary,
  DevBlogHistory,
  DevBlogHistogramBin,
  DevBlogLocPoint,
  DevBlogTimelinePoint,
} from '../../types/devBlog';
import { iconForField, iconForScope } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { MetricCard } from '../shared/MetricCard';
import { SemanticLabel } from '../shared/SemanticIcon';

const chartColors = ['#62d8e6', '#50d38d', '#f0bd5f', '#a78bfa', '#ff6b6b', '#78a8ff', '#d2f970', '#ff9f7a'];

export function DevBlogOverview() {
  const history = useAsyncResource(fetchDevBlogHistory, []);

  if (history.loading) {
    return <EmptyState code="READING MASTER">Scanning Git history from master.</EmptyState>;
  }

  if (history.error || !history.data) {
    return <EmptyState code="DEV BLOG UNAVAILABLE">{history.error ?? 'No Git history analytics were returned.'}</EmptyState>;
  }

  return <DevBlogContent history={history.data} />;
}

function DevBlogContent({ history }: { history: DevBlogHistory }) {
  const defaultTags = useMemo(
    () => history.tagSlices.slice(0, 5).map(slice => slice.label),
    [history.tagSlices],
  );
  const [selectedTags, setSelectedTags] = useState<string[]>(defaultTags);
  const topTags = history.tagSlices.slice(0, 12);
  const largeCommits = history.commitSizeHistogram
    .filter(bin => bin.min >= 1000)
    .reduce((total, bin) => total + bin.count, 0);

  const toggleTag = (tag: string) => {
    setSelectedTags(current =>
      current.includes(tag)
        ? current.filter(item => item !== tag)
        : [...current, tag],
    );
  };

  return (
    <div className="dev-blog-overview">
      <header className="dev-blog-hero system-card">
        <div>
          <span className="eyebrow">Repository narrative</span>
          <h2><SemanticLabel icon={iconForScope('dev_blog')} size="sm"><span>DEV BLOG</span></SemanticLabel></h2>
          <p>Git-derived analytics from every commit reachable from <code>{history.scannedRef}</code>. The charts are read-only archaeology for spotting product surges, risky commit shapes, and writing lanes.</p>
        </div>
        <div className="scope-boundary-strip dev-blog-boundary-strip">
          <span>Commit timeline</span>
          <span>Topic tags</span>
          <span>LOC growth</span>
          <span>Editorial leads</span>
        </div>
        <div className="info-hero-metrics">
          <MetricCard label={metricLabel('commits', 'Commits')} value={formatNumber(history.commitCount)} />
          <MetricCard label={metricLabel('loc', 'Net LOC')} value={formatSigned(history.netLoc)} tone={history.netLoc >= 0 ? 'ok' : 'warn'} />
          <MetricCard label={metricLabel('churn', 'Median churn')} value={formatNumber(history.medianChurn)} />
        </div>
      </header>

      <section className="system-grid info-metric-grid">
        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Master span</span>
            <h2>History window</h2>
          </div>
          <div className="metric-grid">
            <MetricCard label={metricLabel('first_commit', 'First commit')} value={formatDate(history.firstCommitAt)} />
            <MetricCard label={metricLabel('last_commit', 'Latest commit')} value={formatDate(history.lastCommitAt)} />
            <MetricCard label={metricLabel('files', 'File touches')} value={formatNumber(history.totalFilesChanged)} />
            <MetricCard label={metricLabel('additions', 'Additions')} value={formatNumber(history.totalAdditions)} tone="ok" />
            <MetricCard label={metricLabel('deletions', 'Deletions')} value={formatNumber(history.totalDeletions)} tone={history.totalDeletions > 0 ? 'warn' : 'neutral'} />
            <MetricCard label={metricLabel('large_commits', 'Large commits')} value={largeCommits} tone={largeCommits > 0 ? 'warn' : 'ok'} />
          </div>
          <div className="dev-blog-source-line">
            <span>Source</span>
            <code>{history.source}</code>
          </div>
          <div className="dev-blog-source-line">
            <span>Repo</span>
            <code>{history.repositoryRoot}</code>
          </div>
        </div>

        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Editorial suggestions</span>
            <h2>Creative next moves</h2>
          </div>
          <div className="dev-blog-suggestions">
            {history.suggestions.map(suggestion => (
              <article key={suggestion}>
                <SemanticLabel icon={iconForField('advice')}><strong>Suggestion</strong></SemanticLabel>
                <p>{suggestion}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      <DisclosureSection title={<SemanticLabel icon={iconForField('timeline')}><span>Topic timeline</span></SemanticLabel>} defaultOpen meta={`${topTags.length} top tags`}>
        <div className="tag-toggle-row" aria-label="Toggle tags in timeline">
          <button type="button" onClick={() => setSelectedTags(defaultTags)}>Top 5</button>
          <button type="button" onClick={() => setSelectedTags(topTags.map(tag => tag.label))}>All shown</button>
          {topTags.map((tag, index) => (
            <button
              key={tag.label}
              type="button"
              aria-pressed={selectedTags.includes(tag.label)}
              className={selectedTags.includes(tag.label) ? 'active' : ''}
              onClick={() => toggleTag(tag.label)}
            >
              <span style={{ backgroundColor: chartColor(index) }} />
              {tag.label}
            </button>
          ))}
        </div>
        <TagTimelineChart points={history.tagTimeline} selectedTags={selectedTags} tagOrder={topTags.map(tag => tag.label)} />
      </DisclosureSection>

      <section className="dev-blog-chart-grid">
        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Commit size</span>
            <h2>Histogram</h2>
          </div>
          <CommitHistogram bins={history.commitSizeHistogram} />
        </div>

        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Line count</span>
            <h2>LOC growth over time</h2>
          </div>
          <LocGrowthChart points={history.locGrowth} />
        </div>
      </section>

      <DisclosureSection title={<SemanticLabel icon={iconForField('category')}><span>Pie charts</span></SemanticLabel>} defaultOpen meta="area, author, topic">
        <div className="dev-blog-pie-grid">
          <DonutChart
            title="Churn by area"
            slices={history.areaSummaries.slice(0, 7).map(area => ({
              label: area.area,
              value: area.churn,
              note: `${formatNumber(area.commitCount)} commits`,
            }))}
          />
          <DonutChart
            title="Commits by author"
            slices={history.authorSlices.slice(0, 7).map(author => ({
              label: author.label,
              value: author.count,
              note: `${formatNumber(author.churn)} lines`,
            }))}
          />
          <DonutChart
            title="Topic hit share"
            slices={history.tagSlices.slice(0, 7).map(tag => ({
              label: tag.label,
              value: tag.count,
              note: `${formatNumber(tag.churn)} lines`,
            }))}
          />
        </div>
      </DisclosureSection>

      <DisclosureSection title={<SemanticLabel icon={iconForField('commit')}><span>Largest commits</span></SemanticLabel>} meta={`${history.largestCommits.length} commits`}>
        <LargestCommitTable commits={history.largestCommits} />
      </DisclosureSection>
    </div>
  );
}

function TagTimelineChart({
  points,
  selectedTags,
  tagOrder,
}: {
  points: DevBlogTimelinePoint[];
  selectedTags: string[];
  tagOrder: string[];
}) {
  const tags = selectedTags.filter(tag => tagOrder.includes(tag));
  if (points.length === 0 || tags.length === 0) {
    return <EmptyState code="NO TAGS SELECTED">Select one or more tags to draw the timeline.</EmptyState>;
  }

  const width = 820;
  const height = 280;
  const padding = { top: 18, right: 22, bottom: 34, left: 42 };
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;
  const maxValue = Math.max(1, ...points.flatMap(point => tags.map(tag => point.counts[tag] ?? 0)));

  const xFor = (index: number) => padding.left + (points.length <= 1 ? 0 : (index / (points.length - 1)) * plotWidth);
  const yFor = (value: number) => padding.top + ((maxValue - value) / maxValue) * plotHeight;

  return (
    <div className="dev-chart">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Topic tag counts over time">
        <title>Topic tag counts over time</title>
        {[0, 0.5, 1].map(step => {
          const y = padding.top + step * plotHeight;
          return <line key={step} x1={padding.left} x2={width - padding.right} y1={y} y2={y} className="chart-grid-line" />;
        })}
        <text x={padding.left} y={height - 10}>{points[0]?.date}</text>
        <text x={width - padding.right} y={height - 10} textAnchor="end">{points[points.length - 1]?.date}</text>
        <text x={8} y={padding.top + 4}>{maxValue}</text>
        <text x={8} y={padding.top + plotHeight}>0</text>
        {tags.map(tag => {
          const color = chartColor(tagOrder.indexOf(tag));
          const path = points
            .map((point, index) => `${index === 0 ? 'M' : 'L'} ${xFor(index).toFixed(1)} ${yFor(point.counts[tag] ?? 0).toFixed(1)}`)
            .join(' ');

          return (
            <g key={tag}>
              <path d={path} fill="none" stroke={color} strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
              {points.map((point, index) => (
                <circle key={`${tag}-${point.date}`} cx={xFor(index)} cy={yFor(point.counts[tag] ?? 0)} r="4" fill={color}>
                  <title>{`${tag} on ${point.date}: ${point.counts[tag] ?? 0} commits`}</title>
                </circle>
              ))}
            </g>
          );
        })}
      </svg>
    </div>
  );
}

function LocGrowthChart({ points }: { points: DevBlogLocPoint[] }) {
  if (points.length === 0) {
    return <EmptyState code="NO LOC DATA">Git numstat did not return text line counts.</EmptyState>;
  }

  const width = 820;
  const height = 260;
  const padding = { top: 18, right: 22, bottom: 34, left: 54 };
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;
  const totals = points.map(point => point.total);
  const nets = points.map(point => point.net);
  const maxValue = Math.max(...totals, ...nets, 0);
  const minValue = Math.min(...totals, ...nets, 0);
  const range = Math.max(1, maxValue - minValue);
  const xFor = (index: number) => padding.left + (points.length <= 1 ? 0 : (index / (points.length - 1)) * plotWidth);
  const yFor = (value: number) => padding.top + ((maxValue - value) / range) * plotHeight;
  const zeroY = yFor(0);
  const barWidth = Math.max(4, Math.min(18, plotWidth / Math.max(1, points.length) * 0.45));
  const totalPath = points
    .map((point, index) => `${index === 0 ? 'M' : 'L'} ${xFor(index).toFixed(1)} ${yFor(point.total).toFixed(1)}`)
    .join(' ');

  return (
    <div className="dev-chart">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Net LOC growth over time">
        <title>Net LOC growth over time</title>
        <line x1={padding.left} x2={width - padding.right} y1={zeroY} y2={zeroY} className="chart-zero-line" />
        {points.map((point, index) => {
          const x = xFor(index) - barWidth / 2;
          const y = yFor(Math.max(point.net, 0));
          const h = Math.abs(yFor(point.net) - zeroY);
          return (
            <rect
              key={point.date}
              x={x}
              y={point.net >= 0 ? y : zeroY}
              width={barWidth}
              height={Math.max(1, h)}
              rx="2"
              className={point.net >= 0 ? 'loc-bar-add' : 'loc-bar-delete'}
            >
              <title>{`${point.date}: ${formatSigned(point.net)} net LOC, ${point.commitCount} commits`}</title>
            </rect>
          );
        })}
        <path d={totalPath} fill="none" stroke="#62d8e6" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
        {points.map((point, index) => (
          <circle key={`${point.date}-total`} cx={xFor(index)} cy={yFor(point.total)} r="3.5" fill="#62d8e6">
            <title>{`${point.date}: ${formatNumber(point.total)} cumulative net LOC`}</title>
          </circle>
        ))}
        <text x={padding.left} y={height - 10}>{points[0]?.date}</text>
        <text x={width - padding.right} y={height - 10} textAnchor="end">{points[points.length - 1]?.date}</text>
        <text x={8} y={padding.top + 4}>{formatNumber(maxValue)}</text>
        <text x={8} y={padding.top + plotHeight}>{formatNumber(minValue)}</text>
      </svg>
    </div>
  );
}

function CommitHistogram({ bins }: { bins: DevBlogHistogramBin[] }) {
  const maxCount = Math.max(1, ...bins.map(bin => bin.count));

  return (
    <div className="histogram-bars">
      {bins.map((bin, index) => (
        <div className="histogram-row" key={bin.label}>
          <span>{bin.label}</span>
          <div className="histogram-track">
            <div style={{ width: `${Math.max(4, (bin.count / maxCount) * 100)}%`, backgroundColor: chartColor(index) }} />
          </div>
          <strong>{bin.count}</strong>
        </div>
      ))}
    </div>
  );
}

interface PieSlice {
  label: string;
  value: number;
  note: string;
}

function DonutChart({ slices, title }: { slices: PieSlice[]; title: string }) {
  const filtered = slices.filter(slice => slice.value > 0);
  const total = filtered.reduce((sum, slice) => sum + slice.value, 0);
  let offset = 0;

  return (
    <section className="donut-panel">
      <h3>{title}</h3>
      {total === 0 ? (
        <div className="muted-row">No chart data.</div>
      ) : (
        <div className="donut-layout">
          <svg viewBox="0 0 140 140" role="img" aria-label={title}>
            <title>{title}</title>
            <circle cx="70" cy="70" r="46" pathLength="100" className="donut-backdrop" />
            {filtered.map((slice, index) => {
              const percent = slice.value / total * 100;
              const dashOffset = -offset;
              offset += percent;
              return (
                <circle
                  key={slice.label}
                  cx="70"
                  cy="70"
                  r="46"
                  pathLength="100"
                  className="donut-slice"
                  stroke={chartColor(index)}
                  strokeDasharray={`${percent} ${100 - percent}`}
                  strokeDashoffset={dashOffset}
                >
                  <title>{`${slice.label}: ${formatNumber(slice.value)} (${Math.round(percent)}%)`}</title>
                </circle>
              );
            })}
            <text x="70" y="68" textAnchor="middle">{formatNumber(total)}</text>
            <text x="70" y="84" textAnchor="middle">total</text>
          </svg>
          <div className="donut-legend">
            {filtered.map((slice, index) => (
              <div key={slice.label}>
                <span style={{ backgroundColor: chartColor(index) }} />
                <strong>{slice.label}</strong>
                <small>{formatNumber(slice.value)} / {slice.note}</small>
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

function LargestCommitTable({ commits }: { commits: DevBlogCommitSummary[] }) {
  if (commits.length === 0) {
    return <EmptyState code="NO COMMITS">No commits were returned from master.</EmptyState>;
  }

  return (
    <div className="dev-commit-table">
      {commits.map(commit => (
        <article key={commit.hash} className="dev-commit-row">
          <div>
            <code>{commit.shortHash}</code>
            <span>{formatDate(commit.at)}</span>
          </div>
          <div>
            <strong>{commit.subject}</strong>
            <small>{commit.author} - {commit.tags.slice(0, 4).join(', ')}</small>
          </div>
          <div>
            <span>+{formatNumber(commit.additions)}</span>
            <span>-{formatNumber(commit.deletions)}</span>
            <span>{formatNumber(commit.filesChanged)} files</span>
          </div>
        </article>
      ))}
    </div>
  );
}

function metricLabel(key: string, label: string) {
  return <SemanticLabel icon={iconForField(key)}><span>{label}</span></SemanticLabel>;
}

function chartColor(index: number): string {
  return chartColors[((index % chartColors.length) + chartColors.length) % chartColors.length];
}

function formatDate(value: string | null): string {
  if (!value) return 'n/a';
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(new Date(value));
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat().format(value);
}

function formatSigned(value: number): string {
  return `${value >= 0 ? '+' : ''}${formatNumber(value)}`;
}
