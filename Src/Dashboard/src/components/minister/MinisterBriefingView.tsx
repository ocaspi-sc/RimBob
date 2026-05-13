import { fetchBriefing } from '../../api/client';
import type { ScopeConfig } from '../../dashboard/scopes';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { CoverageBadge } from '../shared/DataCoverage';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, summarizeValue } from '../shared/JsonTree';

export function MinisterBriefingView({ scope }: { scope: ScopeConfig }) {
  const briefing = useAsyncResource(signal => fetchBriefing(scope.key, signal), [scope.key]);

  if (scope.status !== 'live') {
    return <EmptyState code="BRIEFING NOT WIRED">{scope.label} is planned but has no briefing endpoint yet.</EmptyState>;
  }

  if (briefing.loading) {
    return <EmptyState code="BRIEFING">Loading latest briefing.</EmptyState>;
  }

  if (briefing.error || briefing.data === null) {
    return <EmptyState code="BRIEFING FAILED">{briefing.error ?? 'No briefing returned.'}</EmptyState>;
  }

  const groups = groupBriefing(scope.key, briefing.data);
  const coverageRows = extractCoverage(briefing.data);

  return (
    <div className="minister-view briefing-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2>Briefing</h2>
        <p>Readable source data behind this minister's reasoning.</p>
      </header>

      {coverageRows.length > 0 && (
        <section className="coverage-strip">
          {coverageRows.map(row => (
            <span key={row.label}>
              {row.label}
              <CoverageBadge state={row.ok ? 'available' : 'missing'} />
            </span>
          ))}
        </section>
      )}

      {groups.map(group => (
        <DisclosureSection
          key={group.title}
          title={group.title}
          defaultOpen={group.defaultOpen}
          meta={summarizeValue(group.value)}
        >
          <JsonTree value={group.value} />
        </DisclosureSection>
      ))}
    </div>
  );
}

interface BriefingGroup {
  title: string;
  value: unknown;
  defaultOpen?: boolean;
}

function groupBriefing(scope: string, briefing: unknown): BriefingGroup[] {
  if (!isRecord(briefing)) {
    return [{ title: 'Briefing payload', value: briefing, defaultOpen: true }];
  }

  if (scope === 'food') {
    return [
      pickGroup('Food status', briefing, ['estimatedDaysOfFood', 'nutritionSource', 'reportedNutrition', 'fallbackNutrition', 'foodUnits', 'mealsCount', 'rawFoodCount', 'colonistCount'], true),
      pickGroup('Crops', briefing, ['readyToHarvest', 'cropBreakdown', 'cropZoneSummaries']),
      pickGroup('Wild harvest', briefing, ['wildHarvestCandidates', 'wildHarvestClusters', 'wildAnimalCount']),
      pickGroup('Skills and labor signals', briefing, ['skills']),
      pickGroup('Infrastructure and storage', briefing, ['infrastructure', 'storage', 'stockpileCells']),
      pickGroup('Kitchen and butchery', briefing, ['kitchen']),
      pickGroup('Data coverage', briefing, ['dataCoverage']),
      pickGroup('Recent incidents', briefing, ['recentFoodIncidents', 'activeThreat']),
      { title: 'Raw remaining fields', value: omitKeys(briefing, [
        'estimatedDaysOfFood', 'nutritionSource', 'reportedNutrition', 'fallbackNutrition', 'foodUnits', 'mealsCount',
        'rawFoodCount', 'colonistCount', 'readyToHarvest', 'cropBreakdown', 'cropZoneSummaries',
        'wildHarvestCandidates', 'wildHarvestClusters', 'wildAnimalCount', 'skills', 'infrastructure',
        'storage', 'stockpileCells', 'kitchen', 'dataCoverage', 'recentFoodIncidents', 'activeThreat',
      ]) },
    ].filter(group => hasContent(group.value));
  }

  return [
    pickGroup('Overview', briefing, ['briefingVersion', 'date', 'gameTick', 'season'], true),
    pickGroup('People', briefing, ['colonists', 'skills', 'traits', 'medical', 'prisoners']),
    pickGroup('Food and resources', briefing, ['food', 'resources']),
    pickGroup('Infrastructure', briefing, ['power', 'buildings']),
    pickGroup('Welfare and threat', briefing, ['mood', 'threat', 'wealth']),
    pickGroup('Environment', briefing, ['weather']),
    pickGroup('Research', briefing, ['research']),
    { title: 'Raw remaining fields', value: omitKeys(briefing, [
      'briefingVersion', 'date', 'gameTick', 'season', 'colonists', 'skills', 'traits', 'medical',
      'prisoners', 'food', 'resources', 'power', 'buildings', 'mood', 'threat', 'wealth',
      'weather', 'research',
    ]) },
  ].filter(group => hasContent(group.value));
}

function extractCoverage(briefing: unknown): Array<{ label: string; ok: boolean }> {
  if (!isRecord(briefing) || !isRecord(briefing.dataCoverage)) return [];
  return Object.entries(briefing.dataCoverage).map(([key, value]) => ({
    label: humanize(key),
    ok: value === true,
  }));
}

function pickGroup(title: string, source: Record<string, unknown>, keys: string[], defaultOpen = false): BriefingGroup {
  const picked = keys.reduce<Record<string, unknown>>((acc, key) => {
    if (key in source) acc[key] = source[key];
    return acc;
  }, {});
  return { title, value: picked, defaultOpen };
}

function omitKeys(source: Record<string, unknown>, keys: string[]): Record<string, unknown> {
  const omitted = new Set(keys);
  return Object.entries(source).reduce<Record<string, unknown>>((acc, [key, value]) => {
    if (!omitted.has(key)) acc[key] = value;
    return acc;
  }, {});
}

function hasContent(value: unknown): boolean {
  return isRecord(value) ? Object.keys(value).length > 0 : value !== undefined && value !== null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function humanize(key: string): string {
  return key
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, char => char.toUpperCase());
}
