import { useMemo, useState } from 'react';
import { fetchDevBlogHistory } from '../../api/devBlog';
import type {
  DevBlogDailyAreaRow,
  DevBlogHistory,
  DevBlogHistogramBin,
  DevBlogLaneCommit,
  DevBlogLocPoint,
} from '../../types/devBlog';
import type { DashboardViewDefinition, DashboardViewKey } from '../../dashboard/scopes';
import { iconForField, iconForScope, type SemanticIconSpec } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { MetricCard } from '../shared/MetricCard';
import { SemanticIconCue, SemanticLabel } from '../shared/SemanticIcon';
import { ViewTabs } from '../layout/ViewTabs';

const chartColors = ['#62d8e6', '#50d38d', '#f0bd5f', '#a78bfa', '#ff6b6b', '#78a8ff', '#d2f970', '#ff9f7a'];
const manualCommitTitles: Record<string, string> = {
  '013ba0fd': 'Chrome tray',
  '013f7444': 'Briefing cache',
  '01a776d4': 'Food step cleanup',
  '02063817': 'Resource requests',
  '02118ea3': 'Food replay',
  '02c16ba3': 'Zone ownership',
  '036c8031': 'JSON icons',
  '03b6c797': 'Tray sync',
  '058a3fbe': 'Mayor fallback',
  '065bb9c1': 'Wizardly branch sync',
  '083a2477': 'Execution board',
  '08d287e3': 'Tasks',
  '08d52d71': 'Launcher docs',
  '09fe3d4c': 'Minister registry',
  '0b5f3b40': 'Mayor data',
  '0df73215': 'Manual trigger',
  '0dfae3ca': 'Advice steps',
  '0e91a2b7': 'Info highlights',
  '0ea4f797': 'Write invariant',
  '0f86c3ef': 'Food infographics',
  '112f59d3': 'Todo notes',
  '118147f1': 'Build verified',
  '1257a11d': 'Food verification',
  '12a1e0fe': 'Rule provenance',
  '1534ab27': 'Agenda replay',
  '1737c66b': 'Agenda pivot',
  '17d254c0': 'Design refresh',
  '18ac4d14': 'Agenda cleanup',
  '196c98e1': 'Mayor rules',
  '1b35f1f8': 'Warm failures',
  '1cd235d9': 'Cooking station',
  '1d6b3b0f': 'Route variations',
  '1f68138e': 'Workspace commit',
  '2032c018': 'Worktree rules',
  '21424d4b': 'Crop nutrition',
  '21fd6295': 'Manual controls',
  '25998a9f': 'RimMind ideas',
  '26d72932': 'Food replay test',
  '2b46e618': 'Cross-agent skills',
  '2be8e112': 'Dev Blog',
  '2bf0dbc8': 'JSON inspectors',
  '2bfff1a5': 'Apply todo',
  '2db72a2c': 'RIMAPI fork',
  '2de06634': 'Mayor tests',
  '3403633e': 'Food summaries',
  '341f84ed': 'Agent instructions',
  '36108510': 'Claude notes',
  '368e5e91': 'Forage filter',
  '38c65ca2': 'Agenda wiring',
  '38f8faf0': 'Agent workflow',
  '392d79dc': 'Terrain thumbnails',
  '395684c7': 'Output plan',
  '3ec1510b': 'Info analytics',
  '41aa5654': 'Dashboard root',
  '42187b0a': 'Warm retry',
  '4233e310': 'Forage targets',
  '43084e86': 'Dynamic inspectors',
  '446c61a1': 'Cook bill',
  '456166ab': 'Command center',
  '4646f7ff': 'Design framing',
  '46733eaa': 'Gemini fallback',
  '4684b25e': 'Port reserve',
  '490df852': 'Cached icons',
  '4a420207': 'Icon categories',
  '4c3413a0': 'AGENTS update',
  '512c07cc': 'Advice priority',
  '5358816e': 'Icon strip',
  '55420343': 'Path migration',
  '582d49d4': 'Food wording',
  '58880b11': 'Advice snapshots',
  '58ca5e0c': 'Durable docs',
  '593a6d15': 'RIMAPI retry',
  '5a667b2f': 'Dashboard boundaries',
  '5a732206': 'Closeout skill',
  '5ab97da5': 'Roadmap pivot',
  '5c3bd31a': 'Food alerts',
  '5c89f1a8': 'Willie plan',
  '5d05b9ba': 'Dashboard v2',
  '605b29eb': 'Todo workflow',
  '649b49df': 'Agenda naming',
  '64a5bd14': 'Claude tier',
  '65a85af9': 'Tray launcher',
  '65b7809b': 'Work diagram',
  '6617c820': 'Plan revert',
  '662e7e41': 'Tasks',
  '66f05fea': 'Aggregate logs',
  '6705720f': 'RIMAPI coverage',
  '67148f18': 'Human note',
  '6784acaf': 'RimBob rename',
  '6a6691db': 'Meal apply',
  '6b9d0937': 'Host endpoints',
  '6e2adbab': 'Icon gateway',
  '7083eb11': 'LLM health',
  '70dadda6': 'Result links',
  '72450988': 'Live operability',
  '72fbda72': 'Agent notes',
  '736ccc10': 'Route concepts',
  '738d6202': 'Gemini key',
  '7461af04': 'Building briefing',
  '74921271': 'Route panel sync',
  '76bcbc85': 'Slash todo',
  '7751f3cd': 'Prompt boxes',
  '77fa7729': 'Worktree ports',
  '7872fa22': 'Gitignore fix',
  '79a02490': 'Strip styling',
  '7ae7f46f': 'Output plan',
  '7ce3d85c': 'Debug endpoints',
  '7e764cce': 'Prompt toggle',
  '7f67d7a3': 'Hunt text',
  '7f830e64': 'Claude notes',
  '803ac1fe': 'RIMAPI gaps',
  '80538019': 'Line endings',
  '81ddb219': 'Food actions',
  '83749294': 'Agenda docs',
  '846ce491': 'Launcher exit',
  '85b084b7': 'Unforbid cleanup',
  '8696a71d': 'RimSage ideas',
  '88361661': 'M0 hardening',
  '8935889f': 'Food diagram',
  '8a9d07eb': 'Repo ideas',
  '8c3c36b2': 'Agent coordination',
  '8c7204d9': 'Forage apply',
  '8cd05dce': 'State spine',
  '8cd5553b': 'Rule diagnostics',
  '90502a3e': 'Trace details',
  '9149f5da': 'Operating notes',
  '93a67b11': 'LLM rules',
  '94ef61db': 'Minister outputs',
  '964acdff': 'Food classification',
  '96b750e6': 'Run skill',
  '97c25ee4': 'Briefing gaps',
  '9ac9dd58': 'Output done',
  '9bce0ce9': 'Food tightening',
  '9d79d4b0': 'SSE agenda',
  '9dd612ec': 'Project references',
  '9e1c8416': 'RAG grounding',
  '9e6782ed': 'Closeout guidance',
  '9f331393': 'State snapshots',
  'a18036b5': 'Food vertical',
  'a1f2aa90': 'AI leads',
  'a1f6dee5': 'Storage posture',
  'a2258ee8': 'Food inspector',
  'a2e5e271': 'LLM inspector',
  'a5b75e08': 'Agenda types',
  'a6a9e0db': 'Endpoint catalog',
  'a6af7c02': 'Crop candidates',
  'a71ceaa9': 'Harvest apply',
  'aad872fd': 'Crop math',
  'abb8cf2d': 'Branch workflow',
  'acd668ef': 'Icon strip',
  'ae0717ae': 'Startup Mayor',
  'ae74606c': 'Icon inventory',
  'af109455': 'Agent pushback',
  'af976c3f': 'Icon cache',
  'afcf2270': 'Rename follow-up',
  'b205b3a2': 'AGENTS update',
  'b4856e32': 'Claude notes',
  'b565d954': 'Disclosure boxes',
  'b5c46f13': 'Split-screen spacing',
  'b8811bc4': 'Refactoring plan',
  'b88ee785': 'Emoji cues',
  'b8f4f639': 'Test inventory',
  'b99f4b57': 'Stale captures',
  'ba76bfcf': 'Agenda bootstrap',
  'bea9a38a': 'Replay metadata',
  'c0ad97d3': 'Advice bus',
  'c2753ec4': 'Merged diagrams',
  'c2bee42d': 'Tray menu',
  'c32fc116': 'Todo skill',
  'c439c922': 'Log paths',
  'c465d4eb': 'Replay logging',
  'c4702aa3': 'Cleanup todo',
  'c6068ea4': 'RAG todo',
  'c66999b0': 'Hunt apply',
  'c6eca9d5': 'Hunting advice',
  'c74fa3af': 'Closeout validation',
  'c7dd895f': 'Route cards',
  'c87b4b19': 'Refine skill',
  'c9cc5ff2': 'Raw tab',
  'ca77666c': 'Agent simplification',
  'cb2a5c09': 'Action ownership',
  'cb7080d8': 'Prompt inspector',
  'cc424edf': 'Food chain',
  'cce21fb5': 'Cabinet sidebar',
  'cd97c2fc': 'Unknown units',
  'cdacfb2a': 'Forbidden food',
  'cddcc17d': 'Derivation helpers',
  'cdffa092': 'Placeholder icons',
  'cedd93f4': 'Typed variables',
  'cf93b7ce': 'Action rename',
  'd08e52c7': 'Diagnostics widths',
  'd13c8f28': 'Console tabs',
  'd2b80ba7': 'Claude notes',
  'd2cf5f46': 'Style warnings',
  'd620c4ac': 'AGENTS update',
  'd757e18c': 'Advice icons',
  'd78c8b4e': 'RIMAPI handshake',
  'd7e1feff': 'Advice freshness',
  'df2cee12': 'Wake context',
  'df4fea40': 'Output merge',
  'e1076e4c': 'AGENTS update',
  'e2a50403': 'Route panel sync',
  'e3cc0ed6': 'Runtime paths',
  'e466a5db': 'Crop payloads',
  'e53335e4': 'Dev Blog trim',
  'e541f1ce': 'Closeout workflow',
  'e5835830': 'Advice normalization',
  'e59c1806': 'Project scaffold',
  'e7184776': 'Food table',
  'e79347af': 'Repo layout',
  'edee7f8c': 'Rollout docs',
  'edfe2c4d': 'RAG retrieval',
  'efd7f257': 'Log lock',
  'f2a1feac': 'Briefing logs',
  'f3ebac72': 'Route panel sync',
  'f49fe55f': 'Replay corpus',
  'f505142e': 'Crash modal',
  'f6a80f9f': 'Food rename',
  'f6f1f616': 'Apply scope',
  'f7e2a7da': 'Aggregate mappers',
  'fb3b3985': 'Fork todos',
  'fd27cab4': 'Priority enum',
  'fd724b42': 'Union state',
  'fd8bfd3d': 'Icon warming',
  'fdf202f4': 'Terrain math',
  'fe4f3396': 'Output sync',
  'ff687e0b': 'Warm hardening',
};
export function DevBlogOverview({
  onSelectView,
  selectedView,
  views,
}: {
  onSelectView: (view: DashboardViewKey) => void;
  selectedView: DashboardViewKey;
  views: DashboardViewDefinition[];
}) {
  const history = useAsyncResource(fetchDevBlogHistory, []);

  if (history.loading) {
    return <EmptyState code="READING MASTER">Scanning Git history from master.</EmptyState>;
  }

  if (history.error || !history.data) {
    return <EmptyState code="DEV BLOG UNAVAILABLE">{history.error ?? 'No Git history analytics were returned.'}</EmptyState>;
  }

  return (
    <DevBlogContent
      history={history.data}
      selectedView={selectedView}
      views={views}
      onSelectView={onSelectView}
    />
  );
}

function DevBlogContent({
  history,
  onSelectView,
  selectedView,
  views,
}: {
  history: DevBlogHistory;
  onSelectView: (view: DashboardViewKey) => void;
  selectedView: DashboardViewKey;
  views: DashboardViewDefinition[];
}) {
  const largeCommits = history.commitSizeHistogram
    .filter(bin => bin.min >= 1000)
    .reduce((total, bin) => total + bin.count, 0);

  return (
    <div className="dev-blog-overview">
      <header className="dev-blog-hero system-card">
        <div>
          <span className="eyebrow">Repository narrative</span>
          <h2><SemanticLabel icon={iconForScope('dev_blog')} size="sm"><span>DEV BLOG</span></SemanticLabel></h2>
          <p>Git-derived analytics from every commit reachable from <code>{history.scannedRef}</code>. The charts are read-only archaeology for spotting product surges, risky commit shapes, and writing lanes.</p>
        </div>
        <div className="scope-boundary-strip dev-blog-boundary-strip">
          <span>Feature tags</span>
          <span>Tag timeline</span>
          <span>LOC growth</span>
          <span>Editorial leads</span>
        </div>
        <div className="info-hero-metrics">
          <MetricCard label={metricLabel('commits', 'Commits')} value={formatNumber(history.commitCount)} />
          <MetricCard label={metricLabel('loc', 'Net LOC')} value={formatSigned(history.netLoc)} tone={history.netLoc >= 0 ? 'ok' : 'warn'} />
          <MetricCard label={metricLabel('churn', 'Median churn')} value={formatNumber(history.medianChurn)} />
          <MetricCard label={metricLabel('scope', 'Scope points')} value={formatNumber(history.dailyVelocity.reduce((sum, point) => sum + point.uniqueScopePoints, 0))} />
        </div>
      </header>

      <ViewTabs
        activeView={selectedView}
        ariaLabel="DEV BLOG history views"
        views={views}
        onSelect={onSelectView}
      />

      {selectedView === 'commits' && (
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
      </section>
      )}

      {selectedView === 'suggestions' && (
      <section className="system-grid info-metric-grid">
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
      )}

      {selectedView === 'features' && (
      <FeatureIndex rows={history.dailyAreaVelocity} />
      )}

      {(selectedView === 'churn' || selectedView === 'commits') && (
      <section className="dev-blog-chart-grid">
        {selectedView === 'commits' && (
        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Commit size</span>
            <h2>Histogram</h2>
          </div>
          <CommitHistogram bins={history.commitSizeHistogram} />
        </div>
        )}

        {selectedView === 'churn' && (
        <div className="system-card">
          <div className="section-heading">
            <span className="eyebrow">Line count</span>
            <h2>LOC growth over time</h2>
          </div>
          <LocGrowthChart points={history.locGrowth} />
        </div>
        )}
      </section>
      )}

      {selectedView === 'topics' && (
      <DisclosureSection title={<SemanticLabel icon={iconForField('category')}><span>Pie charts</span></SemanticLabel>} defaultOpen meta="area, topic">
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
            title="Topic hit share"
            slices={history.tagSlices.slice(0, 7).map(tag => ({
              label: tag.label,
              value: tag.count,
              note: `${formatNumber(tag.churn)} lines`,
            }))}
          />
        </div>
      </DisclosureSection>
      )}
    </div>
  );
}

function FeatureIndex({ rows }: { rows: DevBlogDailyAreaRow[] }) {
  const [selectedTag, setSelectedTag] = useState<string | null>(null);
  const featureModel = useMemo(() => {
    const features = buildFeatureInventory(rows);
    const tagSummaries = buildFeatureTagSummaries(features);
    const tagTimeline = buildFeatureTagTimeline(rows);
    const availableTags = new Set(tagSummaries.map(tag => tag.tag));
    const totalScopePoints = features.reduce((total, feature) => total + feature.scopePoints, 0);

    return {
      availableTags,
      features,
      tagSummaries,
      tagTimeline,
      totalScopePoints,
    };
  }, [rows]);
  const features = featureModel.features;
  const tagSummaries = featureModel.tagSummaries;
  const activeTag = selectedTag && featureModel.availableTags.has(selectedTag) ? selectedTag : null;
  const chartTags = useMemo(
    () => activeTag
      ? [activeTag]
      : tagSummaries.slice(0, Math.min(8, tagSummaries.length)).map(summary => summary.tag),
    [activeTag, tagSummaries],
  );
  const tagOrder = useMemo(() => tagSummaries.map(summary => summary.tag), [tagSummaries]);
  const visibleModel = useMemo(() => {
    const visibleFeatures = activeTag === null
      ? features
      : features.filter(feature => feature.tags.includes(activeTag));
    const visibleScopePoints = visibleFeatures.reduce((total, feature) => total + feature.scopePoints, 0);

    return { visibleFeatures, visibleScopePoints };
  }, [activeTag, features]);
  const visibleFeatures = visibleModel.visibleFeatures;
  const visibleScopePoints = visibleModel.visibleScopePoints;

  if (rows.length === 0) {
    return <EmptyState code="NO FEATURE WORK">No feature activity was generated.</EmptyState>;
  }

  if (features.length === 0) {
    return <EmptyState code="NO FEATURES">No feature buckets were generated for this range.</EmptyState>;
  }

  function selectTag(tag: string) {
    setSelectedTag(tag);
  }

  function clearTags() {
    setSelectedTag(null);
  }

  return (
    <div className="feature-index" aria-label="Feature index filtered by tags">
      <DisclosureSection
        title={<SemanticLabel icon={iconForField('timeline')}><span>Feature tag timeline</span></SemanticLabel>}
        defaultOpen
        meta={activeTag === null ? `${chartTags.length} top tags` : featureTagLabel(activeTag)}
      >
        <FeatureTagTimelineChart
          points={featureModel.tagTimeline}
          selectedTags={chartTags}
          tagOrder={tagOrder}
        />
      </DisclosureSection>
      <div className="feature-index-layout">
        <aside className="feature-filter-sidebar" aria-label="Feature tag filters">
          <div className="feature-filter-sidebar-heading">
            <strong>Tags</strong>
            <small>{activeTag === null ? 'overview' : '1 active'}</small>
          </div>
          <div className="feature-filter-bar">
            <button
              type="button"
              aria-pressed={activeTag === null}
              className={`feature-filter-button ${activeTag === null ? 'active' : ''}`}
              onClick={clearTags}
            >
              <strong>All features</strong>
              <small>{features.length} / {formatNumber(featureModel.totalScopePoints)}</small>
            </button>
            {tagSummaries.map(summary => {
              const active = activeTag === summary.tag;
              return (
                <button
                  key={summary.tag}
                  type="button"
                  aria-pressed={active}
                  className={`feature-filter-button ${active ? 'active' : ''}`}
                  onClick={() => selectTag(summary.tag)}
                >
                  <strong>{featureTagLabel(summary.tag)}</strong>
                  <small>{summary.featureCount} / {formatNumber(summary.scopePoints)}</small>
                </button>
              );
            })}
          </div>
        </aside>
        <div className="feature-index-results">
          <div className="feature-filter-summary">
            <strong>{activeTag === null ? 'All features' : featureTagLabel(activeTag)}</strong>
            <small>{visibleFeatures.length} features / {formatNumber(visibleScopePoints)} score</small>
          </div>
          {visibleFeatures.length === 0 ? (
            <EmptyState code="NO MATCHING FEATURES">No features match the selected tag.</EmptyState>
          ) : (
            <div className="feature-section-list">
              {visibleFeatures.map(feature => (
                <article
                  key={feature.key}
                  className="feature-section-item"
                  title={featureTooltip(feature)}
                  aria-label={featureTooltip(feature)}
                >
                  <div className="feature-rollup-copy">
                    <SemanticIconCue className="feature-rollup-icon" icon={feature.icon} size="md" />
                    <div>
                      <h5>{feature.title}</h5>
                      <p>{feature.description}</p>
                      <div className="feature-tag-list" aria-label={`Tags for ${feature.title}`}>
                        {feature.tags.map(tag => <span key={`${feature.key}-${tag}`}>{featureTagLabel(tag)}</span>)}
                      </div>
                    </div>
                  </div>
                  <div className="feature-rollup-size">
                    <span>Feature score</span>
                    <strong>{formatNumber(feature.scopePoints)}</strong>
                    <small>{feature.sizeLabel} scope</small>
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

interface FeatureSection {
  key: string;
  title: string;
  description: string;
  icon: SemanticIconSpec | undefined;
  scopePoints: number;
  rawScopePoints: number;
  commitBonusPoints: number;
  commitCount: number;
  filesChanged: number;
  sizeLabel: string;
  materialAreas: string[];
  supportingAreas: string[];
  tags: string[];
}

interface FeatureTagSummary {
  tag: string;
  featureCount: number;
  scopePoints: number;
  commitCount: number;
}

interface FeatureTagTimelinePoint {
  date: string;
  scores: Record<string, number>;
  commitCounts: Record<string, number>;
}

interface FeatureBucket {
  domain: string;
  commits: DevBlogLaneCommit[];
}

function buildFeatureInventory(rows: DevBlogDailyAreaRow[]): FeatureSection[] {
  const featureMap = new Map<string, FeatureBucket>();

  for (const row of rows) {
    for (const commit of consolidateRowCommits(row)) {
      const featureTitle = featureGroupTitle(manualCommitTitle(commit));
      const domain = featureDomainTitle(featureTitle);

      let bucket = featureMap.get(featureTitle);
      if (!bucket) {
        bucket = { domain, commits: [] };
        featureMap.set(featureTitle, bucket);
      }

      bucket.commits.push(commit);
    }
  }

  return Array.from(featureMap.entries())
    .map(([title, bucket]) => {
      const display = featureDisplayInfo(title);
      const commitCount = bucket.commits.length;
      const rawScopePoints = bucket.commits.reduce((total, commit) => total + commit.scopePoints, 0);
      const commitBonusPoints = featureCommitBonusPoints(commitCount);
      const scopePoints = rawScopePoints + commitBonusPoints;
      return {
        key: title,
        title: display.title,
        description: display.description,
        icon: display.icon,
        scopePoints,
        rawScopePoints,
        commitBonusPoints,
        commitCount,
        filesChanged: bucket.commits.reduce((total, commit) => total + commit.filesChanged, 0),
        sizeLabel: sizeLabelForScope(scopePoints),
        materialAreas: uniqueStrings(bucket.commits.flatMap(commit => commit.materialAreas)),
        supportingAreas: uniqueStrings(bucket.commits.flatMap(commit => commit.supportingAreas)),
        tags: featureTags(title, bucket.domain),
      };
    })
    .sort((left, right) => right.scopePoints - left.scopePoints || left.title.localeCompare(right.title));
}

function buildFeatureTagSummaries(features: FeatureSection[]): FeatureTagSummary[] {
  const tagMap = new Map<string, FeatureTagSummary>();

  for (const feature of features) {
    for (const tag of feature.tags) {
      const summary = tagMap.get(tag) ?? { tag, featureCount: 0, scopePoints: 0, commitCount: 0 };
      summary.featureCount += 1;
      summary.scopePoints += feature.scopePoints;
      summary.commitCount += feature.commitCount;
      tagMap.set(tag, summary);
    }
  }

  return Array.from(tagMap.values())
    .sort((left, right) => {
      const scoreSort = right.scopePoints - left.scopePoints;
      if (scoreSort !== 0) return scoreSort;
      const domainSort = domainOrder(left.tag) - domainOrder(right.tag);
      if (domainSort !== 0) return domainSort;
      return featureTagLabel(left.tag).localeCompare(featureTagLabel(right.tag));
    });
}

function buildFeatureTagTimeline(rows: DevBlogDailyAreaRow[]): FeatureTagTimelinePoint[] {
  return rows
    .map(row => {
      const scores: Record<string, number> = {};
      const commitCounts: Record<string, number> = {};

      for (const commit of consolidateRowCommits(row)) {
        const featureTitle = featureGroupTitle(manualCommitTitle(commit));
        const domain = featureDomainTitle(featureTitle);
        const tags = featureTags(featureTitle, domain);
        const score = commit.scopePoints + featureCommitBonusPoints(1);

        for (const tag of tags) {
          scores[tag] = (scores[tag] ?? 0) + score;
          commitCounts[tag] = (commitCounts[tag] ?? 0) + 1;
        }
      }

      return {
        date: row.date,
        scores,
        commitCounts,
      };
    })
    .filter(point => Object.keys(point.scores).length > 0);
}

function consolidateRowCommits(row: DevBlogDailyAreaRow): DevBlogLaneCommit[] {
  const byHash = new Map<string, DevBlogLaneCommit[]>();
  for (const commit of row.lanes.flatMap(lane => lane.commits)) {
    const existing = byHash.get(commit.hash);
    if (existing) {
      existing.push(commit);
    } else {
      byHash.set(commit.hash, [commit]);
    }
  }

  return Array.from(byHash.values()).map(mergeCommitInstances);
}

function mergeCommitInstances(commits: DevBlogLaneCommit[]): DevBlogLaneCommit {
  const primary = commits
    .slice()
    .sort((left, right) => right.scopePoints - left.scopePoints || left.shortHash.localeCompare(right.shortHash))[0];
  const scopePoints = commits.reduce((total, commit) => total + commit.scopePoints, 0);

  return {
    ...primary,
    scopePoints,
    sizeLabel: sizeLabelForScope(scopePoints),
    materialAreas: uniqueStrings(commits.flatMap(commit => commit.materialAreas)),
    supportingAreas: uniqueStrings(commits.flatMap(commit => commit.supportingAreas)),
  };
}

function featureTooltip(feature: FeatureSection): string {
  const material = feature.materialAreas.length > 0
    ? `\nMaterial areas: ${feature.materialAreas.join(', ')}`
    : '';
  const supporting = feature.supportingAreas.length > 0
    ? `\nSupporting areas: ${feature.supportingAreas.join(', ')}`
    : '';
  return [
    feature.title,
    feature.description,
    `Tags: ${feature.tags.map(featureTagLabel).join(', ')}`,
    `${feature.sizeLabel} - ${formatNumber(feature.scopePoints)} feature score`,
    `Score: ${formatNumber(feature.rawScopePoints)} raw scope + ${formatNumber(feature.commitBonusPoints)} commit-count bonus`,
    `${formatSourceCommitCount(feature.commitCount)} - ${formatNumber(feature.filesChanged)} files touched`,
  ].join('\n') + material + supporting;
}

function FeatureTagTimelineChart({
  points,
  selectedTags,
  tagOrder,
}: {
  points: FeatureTagTimelinePoint[];
  selectedTags: string[];
  tagOrder: string[];
}) {
  const tags = selectedTags.filter(tag => tagOrder.includes(tag));
  if (points.length === 0 || tags.length === 0) {
    return <EmptyState code="NO TAGS SELECTED">Select one or more feature tags to draw the timeline.</EmptyState>;
  }

  const width = 820;
  const height = 280;
  const padding = { top: 18, right: 22, bottom: 34, left: 42 };
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;
  const maxValue = Math.max(1, ...points.flatMap(point => tags.map(tag => point.scores[tag] ?? 0)));

  const xFor = (index: number) => padding.left + (points.length <= 1 ? 0 : (index / (points.length - 1)) * plotWidth);
  const yFor = (value: number) => padding.top + ((maxValue - value) / maxValue) * plotHeight;

  return (
    <div className="dev-chart">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Feature tag score over time">
        <title>Feature tag score over time</title>
        {[0, 0.5, 1].map(step => {
          const y = padding.top + step * plotHeight;
          return <line key={step} x1={padding.left} x2={width - padding.right} y1={y} y2={y} className="chart-grid-line" />;
        })}
        <text x={padding.left} y={height - 10}>{points[0]?.date}</text>
        <text x={width - padding.right} y={height - 10} textAnchor="end">{points[points.length - 1]?.date}</text>
        <text x={8} y={padding.top + 4}>{formatNumber(maxValue)}</text>
        <text x={8} y={padding.top + plotHeight}>0</text>
        {tags.map(tag => {
          const color = chartColor(tagOrder.indexOf(tag));
          const path = points
            .map((point, index) => `${index === 0 ? 'M' : 'L'} ${xFor(index).toFixed(1)} ${yFor(point.scores[tag] ?? 0).toFixed(1)}`)
            .join(' ');

          return (
            <g key={tag}>
              <path d={path} fill="none" stroke={color} strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
              {points.map((point, index) => (
                <circle key={`${tag}-${point.date}`} cx={xFor(index)} cy={yFor(point.scores[tag] ?? 0)} r="4" fill={color}>
                  <title>{`${featureTagLabel(tag)} on ${point.date}: ${formatNumber(point.scores[tag] ?? 0)} score, ${formatCommitCount(point.commitCounts[tag] ?? 0)}`}</title>
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

function metricLabel(key: string, label: string) {
  return <SemanticLabel icon={iconForField(key)}><span>{label}</span></SemanticLabel>;
}

function chartColor(index: number): string {
  return chartColors[((index % chartColors.length) + chartColors.length) % chartColors.length];
}

function manualCommitTitle(commit: DevBlogLaneCommit): string {
  return manualCommitTitles[commit.shortHash] ?? commit.subject;
}

function featureDisplayInfo(featureKey: string): { title: string; description: string; icon: SemanticIconSpec | undefined } {
  switch (featureKey) {
    case 'Food stock summaries':
      return {
        title: '🍱 Stockpile Truth Ledger',
        description: 'Food-store totals that make meals, raw food, and gaps visible before Chef writes advice.',
        icon: iconForField('food'),
      };
    case 'Stored food classification':
      return {
        title: '🥫 Def-Backed Food Classifier',
        description: 'Stored resources are classified from item defs so real stockpiles drive Food and Mayor food-day estimates.',
        icon: iconForField('has_item_food_classification'),
      };
    case 'Nutrition chain model':
      return {
        title: '⛓️ Nutrition Chain Brain',
        description: 'Rules-first reasoning across raw ingredients, meals, harvest paths, and confidence gaps.',
        icon: iconForField('reported_nutrition'),
      };
    case 'Food table cleanup':
      return {
        title: '📋 Food Ledger Cleanup',
        description: 'Cleaner table presentation for food inventory, advice inputs, and operational readouts.',
        icon: iconForField('data_coverage'),
      };
    case 'Food shortage rules':
      return {
        title: '🚨 Famine Rules Path',
        description: 'Deterministic emergency signals and concrete food-chain actions instead of vague audit memos.',
        icon: iconForField('threat'),
      };
    case 'Food advice polish':
      return {
        title: '✍️ Crisp Food Memos',
        description: 'Sharper player-facing names, unknown-unit handling, and compact Food AdviceItem text.',
        icon: iconForField('summary'),
      };
    case 'Crop candidate ranking':
      return {
        title: '🌾 Grow-Zone Candidate Engine',
        description: 'Grow-zone candidate scoring with crop fit, expected yield, and food-chain value.',
        icon: iconForField('crop_candidates'),
      };
    case 'Terrain fertility math':
      return {
        title: '🌱 Fertility Math Grid',
        description: 'Terrain-aware crop calculations so farm advice reflects where plants will actually grow well.',
        icon: iconForField('terrain_fertility'),
      };
    case 'Crop payload contract':
      return {
        title: '🧾 Crop Payload Contract',
        description: 'Structured crop payloads and coverage fields that make crop advice inspectable.',
        icon: iconForField('crop_breakdown'),
      };
    case 'Nutrition estimates':
      return {
        title: '🍽️ Days-of-Food Estimator',
        description: 'Food-unit and nutrition math that turns inventory and harvest options into days-of-food signals.',
        icon: iconForField('estimated_days_of_food'),
      };
    case 'Safe prey sorter':
      return {
        title: '🎯 Safe Prey Sorter',
        description: 'Rules-path hunt advice that separates low-risk animals from ambiguous or dangerous targets.',
        icon: iconForField('mark_hunt'),
      };
    case 'Forage target picker':
      return {
        title: '🌿 Forage Target Radar',
        description: 'Wild-harvest filtering and target selection that points at reachable edible plants.',
        icon: iconForField('wild_harvest_candidates'),
      };
    case 'Cooking station detection':
      return {
        title: '🔥 Kitchen Detector',
        description: 'Kitchen/cooking-station detection and wording that connects raw food to actual meal production.',
        icon: iconForField('kitchen'),
      };
    case 'Meal bill planning':
      return {
        title: '🍲 Simple-Meal Bill Planner',
        description: 'Simple-meal bill detection and target-count advice for turning ingredients into meals.',
        icon: iconForField('production_bill'),
      };
    case 'Forbidden food handling':
      return {
        title: '⛔ Forbidden Food Gate',
        description: 'Reachability and forbidden-stack signals that explain when visible food is not usable.',
        icon: iconForField('deferred_writes'),
      };
    case 'Storage posture':
      return {
        title: '🧊 Storage Posture Readout',
        description: 'Storage and freezer posture signals for keeping food visible, hauled, and protected.',
        icon: iconForField('kitchen_storage'),
      };
    case 'Route diagrams':
      return {
        title: '🗺️ Food Chain Route Map',
        description: 'Grow, forage, and hunt flow diagrams that show how options enter the shared food chain.',
        icon: iconForField('research'),
      };
    case 'Food route cards':
      return {
        title: '🃏 Grow/Hunt/Forage Cards',
        description: 'Compact route concepts and card variants for comparing grow, forage, and hunt choices.',
        icon: iconForField('research'),
      };
    case 'Food inspector panels':
      return {
        title: '🔎 Food Inspector Panels',
        description: 'Food-specific inspector surfaces that make inputs, summaries, and diagrams easier to audit.',
        icon: iconForField('food'),
      };
    case 'Food infographic variants':
      return {
        title: '📊 Food Infographic Lab',
        description: 'Visual experiments for showing food-chain state without reading raw payloads.',
        icon: iconForField('research'),
      };
    case 'Assisted apply framework':
      return {
        title: '🖱️ Assisted Apply Gate',
        description: 'Shared apply gating, bounded target validation, action vocabulary, and player-confirmed execution rules.',
        icon: iconForField('advice'),
      };
    case 'Harvest and forage apply':
      return {
        title: '🌾 Mark Harvest Pipeline',
        description: 'Player-confirmed harvest and forage designation paths over bounded plant targets.',
        icon: iconForField('mark_harvest'),
      };
    case 'Hunt apply actions':
      return {
        title: '🎯 Mark Hunt Pipeline',
        description: 'Player-confirmed hunting designations with concrete animal targets and safe action payloads.',
        icon: iconForField('mark_hunt'),
      };
    case 'Meal bill apply':
      return {
        title: '🍲 Production Bill Apply',
        description: 'Player-confirmed simple-meal bill creation and target-count updates through RIMAPI.',
        icon: iconForField('kitchen'),
      };
    case 'Mayor agenda lifecycle':
      return {
        title: '🏛️ Mayor Agenda Spine',
        description: 'Agenda bootstrap, naming, replay, persistence, SSE updates, and startup Mayor behavior.',
        icon: iconForField('agenda'),
      };
    case 'Mayor rules and fallback':
      return {
        title: '📜 Mayor Rules Path',
        description: 'Mayor rule coverage, test fixtures, fallback behavior, wake context, and data shaping.',
        icon: iconForScope('mayor'),
      };
    case 'Cabinet direction':
      return {
        title: '🧭 Cabinet Direction Rail',
        description: 'Cabinet sidebar state, union summaries, minister output visibility, and cross-minister direction surfaces.',
        icon: iconForField('cabinet'),
      };
    case 'Advice card state':
      return {
        title: '💬 AdviceItem State Machine',
        description: 'Advice snapshots, priority presentation, freshness handling, step normalization, and warning states.',
        icon: iconForField('advice'),
      };
    case 'Advice controls and icons':
      return {
        title: '🧩 Advice Controls & Icon Cues',
        description: 'Deterministic advice icons, manual trigger controls, resource-request displays, and priority enums.',
        icon: iconForField('advice'),
      };
    case 'Dashboard shell and scopes':
      return {
        title: '🖥️ Command Center Shell',
        description: 'Command center shell, dashboard boundaries, split-screen spacing, disclosure behavior, and scope layout.',
        icon: iconForScope('dev_blog'),
      };
    case 'Inspector and prompt surfaces':
      return {
        title: '🔍 Prompt/Trace Inspectors',
        description: 'JSON inspectors, prompt boxes, LLM/raw panels, trace details, diagnostics tables, and debug endpoint views.',
        icon: iconForField('system_prompt'),
      };
    case 'Dev Blog analytics':
      return {
        title: '📈 Dev Blog Archaeology',
        description: 'Git-derived commit history views, tag timelines, feature rollups, and editorial trimming.',
        icon: iconForScope('dev_blog'),
      };
    case 'Icon cache and gateway':
      return {
        title: '🖼️ Icon Gateway Inventory',
        description: 'Cached item icons, icon gateway routes, category grouping, inventory strips, and JSON-backed icon metadata.',
        icon: iconForField('icon_cache'),
      };
    case 'Icon warming and placeholders':
      return {
        title: '🛡️ Placeholder Filter & Warm Queue',
        description: 'Warm-cache runs, placeholder red-X rejection, retry hardening, failure reporting, and safe fallback behavior.',
        icon: iconForField('error'),
      };
    case 'Terrain and visual thumbnails':
      return {
        title: '🌍 Terrain Thumbnail Grounding',
        description: 'Terrain image handling and visual context assets used by dashboard surfaces.',
        icon: iconForField('growing_terrain'),
      };
    case 'Host health and endpoint coverage':
      return {
        title: '🫀 Host Health Surface',
        description: 'Host endpoint surfaces, health/status metadata, endpoint catalogues, project references, and coverage reporting.',
        icon: iconForField('host'),
      };
    case 'RIMAPI bridge':
      return {
        title: '🔌 RIMAPI Bridge Coverage',
        description: 'RIMAPI handshake, retry behavior, endpoint coverage, fork integration, and gap documentation.',
        icon: iconForField('rimapi'),
      };
    case 'Gemini and LLM status':
      return {
        title: '✨ Gemini/LLM Health',
        description: 'Gemini key handling, fallback paths, LLM health reporting, and request/status diagnostics.',
        icon: iconForField('llm'),
      };
    case 'State snapshot spine':
      return {
        title: '💾 State Store Snapshot Spine',
        description: 'State snapshots, briefing cache, live operability, stale-capture behavior, and state-store spine work.',
        icon: iconForField('has_live_state'),
      };
    case 'Replay corpus and logs':
      return {
        title: '📚 Replay Corpus Ledger',
        description: 'Replay logging, briefing logs, corpus metadata, and historical decision records for refinement.',
        icon: iconForField('logs'),
      };
    case 'Aggregate derivations':
      return {
        title: '🧮 Briefing Aggregate Mappers',
        description: 'Aggregate logs, derivation helpers, mapper cleanup, and computed facts used by briefings.',
        icon: iconForField('data_coverage'),
      };
    case 'Minister output store':
      return {
        title: '🗃️ Minister Output Store',
        description: 'Minister output persistence, output merge/sync plans, registry wiring, and snapshot availability.',
        icon: iconForField('minister'),
      };
    case 'Advice bus and actions':
      return {
        title: '🚌 AdviceBus Write Boundary',
        description: 'Advice bus wiring, action naming/ownership, and invariants around who can request or execute writes.',
        icon: iconForField('advice_item'),
      };
    case 'Rule diagnostics':
      return {
        title: '🧪 Rules Provenance Console',
        description: 'Rule diagnostics tables, provenance metadata, LLM rule notes, and rules-path correctness checks.',
        icon: iconForField('rules'),
      };
    case 'RAG retrieval':
      return {
        title: '📖 RAG Grounding Loop',
        description: 'Guide retrieval, RAG grounding, Claude tier notes, and context paths that feed ministers.',
        icon: iconForField('guide_citations'),
      };
    case 'Agent rules and coordination':
      return {
        title: '🤖 Agent Operating Manual',
        description: 'AGENTS guidance, pushback rules, cross-agent coordination, operating notes, and repo idea routing.',
        icon: iconForField('rules'),
      };
    case 'Agent skill workflows':
      return {
        title: '🛠️ Closeout/Refine/Run Skills',
        description: 'Closeout/refine/run skills, skill simplification, workflow validation, and reusable agent procedures.',
        icon: iconForField('logs'),
      };
    case 'Codex prompt runner':
      return {
        title: '⚙️ Resumable Codex Runner',
        description: 'A reusable prompt-runner skill and script for long Codex jobs that need resumable execution.',
        icon: iconForField('logs'),
      };
    case 'Endpoint timing telemetry':
      return {
        title: '⏱️ Endpoint Timing Footer',
        description: 'Dashboard request timing telemetry that keeps endpoint latency visible while navigating the console.',
        icon: iconForField('status'),
      };
    case 'Worktree and branch operations':
      return {
        title: '🌿 Worktree Merge Hygiene',
        description: 'Worktree rules, port isolation, branch sync, merge hygiene, and branch-workflow repair.',
        icon: iconForField('logs'),
      };
    case 'Design docs and roadmap':
      return {
        title: '🧭 Design Decision Log',
        description: 'Durable design docs, roadmap pivots, Willie plans, product framing, and verification writeups.',
        icon: iconForField('briefing_version'),
      };
    case 'Todo and idea triage':
      return {
        title: '🗂️ Tasks Triage Board',
        description: 'Tasks structure, slash todo capture, execution-board cleanup, and generated RimMind/RimSage idea triage.',
        icon: iconForField('logs'),
      };
    case 'Local runtime and launcher ops':
      return {
        title: '🪵 Local Runtime & Tray Logs',
        description: 'Tray launcher, crash handling, runtime path migration, logs, port reservation, and local process operations.',
        icon: iconForField('status'),
      };
    case 'Repository cleanup and scaffolding':
      return {
        title: '🧱 Repo Scaffold & Fork Hygiene',
        description: 'Repo layout, project scaffold, gitignore/line endings, RIMAPI fork cleanup, test inventory, and typed cleanup.',
        icon: iconForField('files'),
      };
    default:
      return {
        title: featureKey,
        description: 'Curated repository work grouped from related history entries.',
        icon: iconForField('category'),
      };
  }
}

function featureGroupTitle(title: string): string {
  if (title.includes('Delegate Auto execution')) return 'Design docs and roadmap';
  if (title.includes('Trim Auto/HTN scaffolding')) return 'Design docs and roadmap';
  if (title.includes('Make .plans the canonical agent-plan location')) return 'Design docs and roadmap';
  if (title.includes('Harden multi-agent git commits')) return 'Agent rules and coordination';
  if (title.includes('Delete multi-agent git race plan')) return 'Agent rules and coordination';
  if (title.includes('launcher crash exit code')) return 'Local runtime and launcher ops';
  if (title.includes('resumable Codex prompt runner')) return 'Codex prompt runner';
  if (title.includes('endpoint timing footer')) return 'Endpoint timing telemetry';

  switch (title) {
    case 'AGENTS update':
    case 'Agent coordination':
    case 'Agent instructions':
    case 'Agent simplification':
    case 'Agent workflow':
    case 'Claude notes':
    case 'Agent notes':
    case 'Agent pushback':
    case 'Operating notes':
    case 'Pushback guidance':
    case 'Repo ideas':
    case 'Update AGENTS.md':
    case 'Update claude.md':
      return 'Agent rules and coordination';
    case 'Closeout guidance':
    case 'Closeout skill':
    case 'Closeout validation':
    case 'Closeout workflow':
    case 'Cross-agent skills':
    case 'Refine skill':
    case 'Run skill':
      return 'Agent skill workflows';
    case 'Worktree ports':
    case 'Worktree rules':
      return 'Worktree and branch operations';
    case 'Build verified':
    case 'Cleanup todo':
    case 'Execution board':
    case 'Human note':
    case 'Tasks':
    case 'RAG todo':
    case 'RimMind ideas':
    case 'RimSage ideas':
    case 'Slash todo':
    case 'Todo cleanup':
    case 'Todo skill':
    case 'Todo workflow':
    case 'Todo notes':
    case 'Workspace commit':
    case 'Update Tasks.md':
      return 'Todo and idea triage';
    case 'AI leads':
    case 'Briefing gaps':
    case 'Design framing':
    case 'Design refresh':
    case 'Durable docs':
    case 'Food verification':
    case 'Launcher docs':
    case 'Refactoring plan':
    case 'Rollout docs':
    case 'Roadmap pivot':
    case 'Willie plan':
    case 'Plan revert':
    case 'Result links':
    case 'Zone ownership':
      return 'Design docs and roadmap';
    case 'Apply scope':
    case 'Apply todo':
      return 'Assisted apply framework';
    case 'Forage apply':
    case 'Harvest apply':
      return 'Harvest and forage apply';
    case 'Hunt apply':
      return 'Hunt apply actions';
    case 'Meal apply':
      return 'Meal bill apply';
    case 'Cooking station':
      return 'Cooking station detection';
    case 'Cook bill':
      return 'Meal bill planning';
    case 'Forbidden food':
      return 'Forbidden food handling';
    case 'Storage posture':
      return 'Storage posture';
    case 'Crop candidates':
      return 'Crop candidate ranking';
    case 'Crop math':
    case 'Terrain math':
      return 'Terrain fertility math';
    case 'Crop nutrition':
      return 'Nutrition estimates';
    case 'Crop payloads':
      return 'Crop payload contract';
    case 'Forage filter':
    case 'Forage targets':
      return 'Forage target picker';
    case 'Hunting advice':
    case 'Hunt text':
      return 'Safe prey sorter';
    case 'Food summaries':
      return 'Food stock summaries';
    case 'Food classification':
      return 'Stored food classification';
    case 'Food chain':
      return 'Nutrition chain model';
    case 'Food table':
      return 'Food table cleanup';
    case 'Food actions':
    case 'Food alerts':
      return 'Food shortage rules';
    case 'Food rename':
    case 'Food tightening':
    case 'Food wording':
    case 'Unknown units':
      return 'Food advice polish';
    case 'Food diagram':
    case 'Merged diagrams':
    case 'Work diagram':
      return 'Route diagrams';
    case 'Route cards':
    case 'Route concepts':
    case 'Route variations':
    case 'Food vertical':
      return 'Food route cards';
    case 'Food inspector':
      return 'Food inspector panels';
    case 'Food infographics':
      return 'Food infographic variants';
    case 'Food replay':
    case 'Food replay test':
    case 'Food step cleanup':
      return 'Replay corpus and logs';
    case 'Advice freshness':
    case 'Advice normalization':
    case 'Advice priority':
    case 'Advice snapshots':
    case 'Advice steps':
    case 'Style warnings':
      return 'Advice card state';
    case 'Advice icons':
    case 'Manual controls':
    case 'Manual trigger':
    case 'Priority enum':
    case 'Resource requests':
      return 'Advice controls and icons';
    case 'Agenda bootstrap':
    case 'Agenda cleanup':
    case 'Agenda naming':
    case 'Agenda pivot':
    case 'Agenda replay':
    case 'Agenda types':
    case 'Agenda wiring':
    case 'Agenda docs':
    case 'SSE agenda':
    case 'Startup Mayor':
      return 'Mayor agenda lifecycle';
    case 'Mayor data':
    case 'Mayor fallback':
    case 'Mayor rules':
    case 'Mayor tests':
    case 'Wake context':
      return 'Mayor rules and fallback';
    case 'Cabinet sidebar':
    case 'Union state':
      return 'Cabinet direction';
    case 'Minister outputs':
      return 'Minister output store';
    case 'Cached icons':
    case 'Icon cache':
    case 'Icon categories':
    case 'Icon gateway':
    case 'Icon inventory':
    case 'Icon strip':
    case 'JSON icons':
      return 'Icon cache and gateway';
    case 'Icon warming':
    case 'Placeholder icons':
    case 'Warm failures':
    case 'Warm hardening':
    case 'Warm retry':
      return 'Icon warming and placeholders';
    case 'Terrain thumbnails':
      return 'Terrain and visual thumbnails';
    case 'Command center':
    case 'Console tabs':
    case 'Dashboard boundaries':
    case 'Dashboard root':
    case 'Dashboard v2':
    case 'Disclosure boxes':
    case 'Emoji cues':
    case 'Split-screen spacing':
    case 'Strip styling':
      return 'Dashboard shell and scopes';
    case 'Debug endpoints':
    case 'Diagnostics widths':
    case 'Dynamic inspectors':
    case 'Info analytics':
    case 'Info highlights':
    case 'JSON inspectors':
    case 'LLM inspector':
    case 'Prompt boxes':
    case 'Prompt inspector':
    case 'Prompt toggle':
    case 'Raw tab':
    case 'Trace details':
      return 'Inspector and prompt surfaces';
    case 'Dev Blog':
    case 'Dev Blog trim':
      return 'Dev Blog analytics';
    case 'Briefing cache':
    case 'Building briefing':
    case 'Live operability':
    case 'Stale captures':
    case 'State snapshots':
    case 'State spine':
      return 'State snapshot spine';
    case 'Briefing logs':
    case 'Replay corpus':
    case 'Replay logging':
    case 'Replay metadata':
      return 'Replay corpus and logs';
    case 'Aggregate logs':
    case 'Aggregate mappers':
    case 'Derivation helpers':
      return 'Aggregate derivations';
    case 'Action ownership':
    case 'Action rename':
    case 'Advice bus':
    case 'Write invariant':
      return 'Advice bus and actions';
    case 'Minister registry':
    case 'Output done':
    case 'Output merge':
    case 'Output plan':
    case 'Output sync':
      return 'Minister output store';
    case 'Rule diagnostics':
    case 'LLM rules':
    case 'Rule provenance':
      return 'Rule diagnostics';
    case 'Endpoint catalog':
    case 'Host endpoints':
    case 'M0 hardening':
    case 'Project references':
      return 'Host health and endpoint coverage';
    case 'RIMAPI coverage':
    case 'RIMAPI gaps':
    case 'RIMAPI handshake':
    case 'RIMAPI retry':
      return 'RIMAPI bridge';
    case 'Gemini fallback':
    case 'Gemini key':
    case 'LLM health':
      return 'Gemini and LLM status';
    case 'Chrome tray':
    case 'Crash modal':
    case 'Log lock':
    case 'Log paths':
    case 'Path migration':
    case 'Port reserve':
    case 'RimBob rename':
    case 'Runtime paths':
    case 'Tray launcher':
    case 'Tray menu':
    case 'Tray sync':
    case 'Launcher exit':
      return 'Local runtime and launcher ops';
    case 'Fork todos':
    case 'Gitignore fix':
    case 'Line endings':
    case 'Project scaffold':
    case 'Repo layout':
    case 'RIMAPI fork':
    case 'Rename follow-up':
    case 'Test inventory':
    case 'Unforbid cleanup':
    case 'Typed variables':
      return 'Repository cleanup and scaffolding';
    case 'Branch merge':
    case 'Branch workflow':
    case 'Route panel sync':
    case 'Wizardly branch sync':
      return 'Worktree and branch operations';
    case 'Claude tier':
    case 'RAG grounding':
    case 'RAG retrieval':
      return 'RAG retrieval';
    default:
      return title;
  }
}

function featureDomainTitle(featureTitle: string): string {
  switch (featureTitle) {
    case 'Food stock summaries':
    case 'Stored food classification':
    case 'Nutrition chain model':
    case 'Food table cleanup':
    case 'Food shortage rules':
    case 'Food advice polish':
    case 'Crop candidate ranking':
    case 'Terrain fertility math':
    case 'Crop payload contract':
    case 'Nutrition estimates':
    case 'Safe prey sorter':
    case 'Forage target picker':
    case 'Cooking station detection':
    case 'Meal bill planning':
    case 'Forbidden food handling':
    case 'Storage posture':
    case 'Route diagrams':
    case 'Food route cards':
    case 'Food inspector panels':
    case 'Food infographic variants':
      return 'Food';
    case 'Assisted apply framework':
    case 'Harvest and forage apply':
    case 'Hunt apply actions':
    case 'Meal bill apply':
    case 'Advice bus and actions':
      return 'Execution';
    case 'Mayor agenda lifecycle':
    case 'Mayor rules and fallback':
    case 'Cabinet direction':
      return 'Mayor';
    case 'Advice card state':
    case 'Advice controls and icons':
    case 'Rule diagnostics':
      return 'Advice';
    case 'Dashboard shell and scopes':
    case 'Inspector and prompt surfaces':
    case 'Dev Blog analytics':
    case 'Endpoint timing telemetry':
      return 'Dashboard';
    case 'Icon cache and gateway':
    case 'Icon warming and placeholders':
    case 'Terrain and visual thumbnails':
      return 'Icons';
    case 'State snapshot spine':
    case 'Replay corpus and logs':
    case 'Aggregate derivations':
    case 'Minister output store':
      return 'Persistence';
    case 'Host health and endpoint coverage':
    case 'RIMAPI bridge':
    case 'Gemini and LLM status':
    case 'Local runtime and launcher ops':
    case 'Repository cleanup and scaffolding':
      return 'Infra';
    case 'RAG retrieval':
      return 'Advice';
    case 'Design docs and roadmap':
    case 'Todo and idea triage':
      return 'Design';
    case 'Agent rules and coordination':
    case 'Agent skill workflows':
    case 'Codex prompt runner':
    case 'Worktree and branch operations':
      return 'Agent Ops';
    default:
      return 'Infra';
  }
}

function domainOrder(domain: string): number {
  switch (domain) {
    case 'Food':
      return 0;
    case 'Mayor':
      return 1;
    case 'Advice':
      return 2;
    case 'Execution':
      return 3;
    case 'Dashboard':
      return 4;
    case 'Icons':
      return 5;
    case 'Persistence':
      return 6;
    case 'Infra':
      return 7;
    case 'Design':
      return 8;
    case 'Agent Ops':
      return 9;
    default:
      return 10;
  }
}

function featureTags(featureKey: string, domain: string): string[] {
  const tags = new Set<string>();
  const key = featureKey.toLowerCase();
  tags.add(domain);

  if (domain === 'Food') tags.add('Minister');
  if (domain === 'Execution') tags.add('Assisted Apply');
  if (domain === 'Persistence') tags.add('Persistence');
  if (domain === 'Infra') tags.add('Infra');
  if (domain === 'Dashboard') tags.add('Dashboard');
  if (domain === 'Icons') tags.add('Icon Gateway');
  if (domain === 'Advice') tags.add('Advice');
  if (domain === 'Design') tags.add('Design');
  if (domain === 'Agent Ops') tags.add('Agents');

  if (key.includes('route') || key.includes('infographic') || key.includes('thumbnail') || key.includes('card')) tags.add('Visualization');
  if (key.includes('inspector') || key.includes('prompt') || key.includes('trace')) tags.add('Inspection');
  if (key.includes('endpoint') || key.includes('timing')) tags.add('Inspection');
  if (key.includes('rules') || key.includes('sorter') || key.includes('shortage')) tags.add('Rules');
  if (key.includes('rag')) tags.add('RAG');
  if (key.includes('bridge') || key.includes('rimapi') || key.includes('apply')) tags.add('RIMAPI');
  if (key.includes('state') || key.includes('briefing') || key.includes('aggregate') || key.includes('stock') || key.includes('classifier') || key.includes('nutrition')) tags.add('State Store');
  if (key.includes('replay')) tags.add('Replay Corpus');
  if (key.includes('llm') || key.includes('gemini')) tags.add('LLM');
  if (key.includes('design') || key.includes('todo')) tags.add('Docs');
  if (key.includes('agent') || key.includes('worktree')) tags.add('Agents');
  if (key.includes('cleanup') || key.includes('scaffolding') || key.includes('operations')) tags.add('Refactor');
  if (key.includes('advice') || key.includes('meal bill') || key.includes('hunt apply')) tags.add('Advice');
  if (key.includes('crop') || key.includes('fertility') || key.includes('forage') || key.includes('prey')) tags.add('Briefing');

  switch (featureKey) {
    case 'Safe prey sorter':
      tags.add('mark_hunt');
      tags.add('Rules');
      break;
    case 'Forage target picker':
      tags.add('mark_harvest');
      break;
    case 'Meal bill planning':
    case 'Meal bill apply':
      tags.add('production_bill');
      break;
    case 'Mayor agenda lifecycle':
      tags.add('Mayor Agenda');
      tags.add('SSE');
      break;
    case 'Minister output store':
      tags.add('Minister Output');
      break;
    case 'Advice bus and actions':
      tags.add('AdviceBus');
      break;
    case 'Design docs and roadmap':
      tags.add('Decision Log');
      break;
  }

  return Array.from(tags);
}

function featureTagLabel(tag: string): string {
  switch (tag) {
    case 'Advice':
      return '💬 Advice';
    case 'AdviceBus':
      return '🚌 AdviceBus';
    case 'Agent Ops':
      return '🤖 Agent Ops';
    case 'Agents':
      return '🤖 Agents';
    case 'Assisted Apply':
      return '🖱️ Assisted Apply';
    case 'Briefing':
      return '📋 Briefing';
    case 'Dashboard':
      return '🖥️ Dashboard';
    case 'Decision Log':
      return '🧭 Decision Log';
    case 'Design':
      return '🎨 Design';
    case 'Execution':
      return '🖱️ Execution';
    case 'Docs':
      return '📄 Docs';
    case 'Food':
      return '🍱 Food';
    case 'Icon Gateway':
      return '🖼️ Icon Gateway';
    case 'Icons':
      return '🖼️ Icons';
    case 'Infra':
      return '🧱 Infra';
    case 'Inspection':
      return '🔎 Inspection';
    case 'LLM':
      return '✨ LLM';
    case 'Mayor':
      return '🏛️ Mayor';
    case 'Mayor Agenda':
      return '🏛️ Mayor Agenda';
    case 'Minister':
      return '🎩 Minister';
    case 'Minister Output':
      return '🗃️ Minister Output';
    case 'Persistence':
      return '💾 Persistence';
    case 'RAG':
      return '📖 RAG';
    case 'Refactor':
      return '♻️ Refactor';
    case 'Replay Corpus':
      return '📚 Replay Corpus';
    case 'RIMAPI':
      return '🔌 RIMAPI';
    case 'Rules':
      return '📜 Rules';
    case 'SSE':
      return '📡 SSE';
    case 'State Store':
      return '💾 State Store';
    case 'Visualization':
      return '📊 Visualization';
    case 'mark_harvest':
      return '🌾 mark_harvest';
    case 'mark_hunt':
      return '🎯 mark_hunt';
    case 'production_bill':
      return '🍲 production_bill';
    default:
      return tag;
  }
}

function uniqueStrings(values: string[]): string[] {
  return Array.from(new Set(values.filter(Boolean))).sort((left, right) => left.localeCompare(right));
}

function featureCommitBonusPoints(commitCount: number): number {
  return commitCount * 250;
}

function sizeLabelForScope(scopePoints: number): string {
  if (scopePoints <= 12) return 'XS';
  if (scopePoints <= 49) return 'S';
  if (scopePoints <= 199) return 'M';
  if (scopePoints <= 999) return 'L';
  return 'XL';
}

function formatDate(value: string | null): string {
  if (!value) return 'n/a';
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(new Date(value));
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat().format(value);
}

function formatCommitCount(value: number): string {
  return `${formatNumber(value)} ${value === 1 ? 'commit' : 'commits'}`;
}

function formatSourceCommitCount(value: number): string {
  return `${formatNumber(value)} source ${value === 1 ? 'commit' : 'commits'}`;
}

function formatSigned(value: number): string {
  return `${value >= 0 ? '+' : ''}${formatNumber(value)}`;
}
