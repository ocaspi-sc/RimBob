import { fetchBriefing } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForField, iconForSection, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { CoverageBadge } from '../shared/DataCoverage';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { JsonTree, summarizeValue } from '../shared/JsonTree';
import { SemanticLabel } from '../shared/SemanticIcon';
import { FoodCropMathPanel } from './FoodCropMathPanel';

export function MinisterBriefingView({ scope }: { scope: ScopeConfig }) {
  const briefing = useAsyncResource(signal => fetchBriefing(scope.key, signal), [scope.key]);
  const hasSourceBriefing = scope.key === 'welfare';

  if (scope.status !== 'live' && !hasSourceBriefing) {
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
        <h2><SemanticLabel icon={iconForView('briefing')} size="sm"><span>Briefing</span></SemanticLabel></h2>
        <p>Readable source data behind this minister's reasoning.</p>
      </header>

      {coverageRows.length > 0 && (
        <section className="coverage-strip">
          {coverageRows.map(row => (
            <span key={row.key}>
              <SemanticLabel icon={iconForField(row.key)}><span>{row.label}</span></SemanticLabel>
              <CoverageBadge state={row.ok ? 'available' : 'missing'} />
            </span>
          ))}
        </section>
      )}

      {scope.key === 'food' && <FoodCropMathPanel />}

      {groups.map(group => (
        <DisclosureSection
          key={group.key}
          title={<SemanticLabel icon={iconForSection(group.key)}><span>{group.title}</span></SemanticLabel>}
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
  key: string;
  title: string;
  value: unknown;
  defaultOpen?: boolean;
}

function groupBriefing(scope: string, briefing: unknown): BriefingGroup[] {
  if (!isRecord(briefing)) {
    return [{ key: 'raw_payload', title: 'Briefing payload', value: briefing, defaultOpen: true }];
  }

  if (scope === 'food') {
    return [
      pickGroup('food_status', 'Food status', briefing, ['estimatedDaysOfFood', 'nutritionSource', 'reportedNutrition', 'fallbackNutrition', 'foodUnits', 'mealsCount', 'rawFoodCount', 'colonistCount'], true),
      pickGroup('crops', 'Crops', briefing, ['readyToHarvest', 'cropBreakdown', 'cropZoneSummaries']),
      pickGroup('wild_harvest', 'Forage and hunting', briefing, ['wildHarvestCandidates', 'wildHarvestClusters', 'wildAnimalCount']),
      pickGroup('skills_and_labor', 'Skills and labor signals', briefing, ['skills']),
      pickGroup('infrastructure_and_storage', 'Infrastructure and storage', briefing, ['infrastructure', 'storage', 'stockpileCells']),
      pickGroup('kitchen_and_butchery', 'Kitchen and butchery', briefing, ['kitchen']),
      pickGroup('data_coverage', 'Data coverage', briefing, ['dataCoverage']),
      pickGroup('recent_incidents', 'Recent incidents', briefing, ['recentFoodIncidents', 'activeThreat']),
      { key: 'raw_remaining_fields', title: 'Raw remaining fields', value: omitKeys(briefing, [
        'estimatedDaysOfFood', 'nutritionSource', 'reportedNutrition', 'fallbackNutrition', 'foodUnits', 'mealsCount',
        'rawFoodCount', 'colonistCount', 'readyToHarvest', 'cropBreakdown', 'cropZoneSummaries',
        'wildHarvestCandidates', 'wildHarvestClusters', 'wildAnimalCount', 'skills', 'infrastructure',
        'storage', 'stockpileCells', 'kitchen', 'dataCoverage', 'recentFoodIncidents', 'activeThreat',
      ]) },
    ].filter(group => hasContent(group.value));
  }

  if (scope === 'welfare') {
    return [
      pickGroup('overview', 'Overview', briefing, ['briefingVersion', 'gameTick', 'colonistCount'], true),
      pickGroup('mood', 'Mood', briefing, ['mood', 'worstPawns']),
      pickGroup('need_lows', 'Need lows', briefing, ['needLows']),
      pickGroup('rooms', 'Rooms', briefing, ['rooms']),
      pickGroup('data_coverage', 'Data coverage', briefing, ['dataCoverage']),
      { key: 'raw_remaining_fields', title: 'Raw remaining fields', value: omitKeys(briefing, [
        'briefingVersion', 'gameTick', 'colonistCount', 'mood', 'worstPawns', 'needLows', 'rooms', 'dataCoverage',
      ]) },
    ].filter(group => hasContent(group.value));
  }

  if (scope === 'willie') {
    return [
      pickGroup('overview', 'Overview', briefing, ['briefingVersion', 'date', 'gameTick', 'mapId', 'colonistCount'], true),
      pickGroup('power_stability', 'Power Stability', briefing, ['powerStability']),
      pickGroup('thermal_control', 'Thermal Control', briefing, ['thermalControl']),
      pickGroup('functional_rooms', 'Functional Rooms', briefing, ['functionalRooms', 'anchorInventory']),
      pickGroup('storage_placement', 'Storage Placement', briefing, ['storagePlacement']),
      pickGroup('build_queue', 'Build Queue', briefing, ['materialBottleneck', 'stalledBuilds', 'constructionBacklog']),
      pickGroup('layout_and_risk', 'Layout And Risk', briefing, ['baseLayout', 'fireRisk']),
      pickGroup('data_coverage', 'Data Coverage', briefing, ['dataCoverage']),
      { key: 'raw_remaining_fields', title: 'Raw remaining fields', value: omitKeys(briefing, [
        'briefingVersion', 'date', 'gameTick', 'mapId', 'colonistCount', 'powerStability', 'thermalControl',
        'functionalRooms', 'anchorInventory', 'storagePlacement', 'materialBottleneck', 'stalledBuilds',
        'constructionBacklog', 'baseLayout', 'fireRisk', 'dataCoverage',
      ]) },
    ].filter(group => hasContent(group.value));
  }

  return [
    pickGroup('overview', 'Overview', briefing, ['briefingVersion', 'date', 'gameTick', 'season'], true),
    pickGroup('people', 'People', briefing, ['colonists', 'skills', 'traits', 'medical', 'prisoners']),
    pickGroup('food_and_resources', 'Food and resources', briefing, ['food', 'resources']),
    pickGroup('infrastructure', 'Infrastructure', briefing, ['power', 'buildings']),
    pickGroup('welfare_and_threat', 'Welfare and threat', briefing, ['mood', 'threat', 'wealth']),
    pickGroup('environment', 'Environment', briefing, ['weather']),
    pickGroup('research', 'Research', briefing, ['research']),
    { key: 'raw_remaining_fields', title: 'Raw remaining fields', value: omitKeys(briefing, [
      'briefingVersion', 'date', 'gameTick', 'season', 'colonists', 'skills', 'traits', 'medical',
      'prisoners', 'food', 'resources', 'power', 'buildings', 'mood', 'threat', 'wealth',
      'weather', 'research',
    ]) },
  ].filter(group => hasContent(group.value));
}

function extractCoverage(briefing: unknown): Array<{ key: string; label: string; ok: boolean }> {
  if (!isRecord(briefing) || !isRecord(briefing.dataCoverage)) return [];
  return Object.entries(briefing.dataCoverage).map(([key, value]) => ({
    key,
    label: humanize(key),
    ok: value === true,
  }));
}

function pickGroup(key: string, title: string, source: Record<string, unknown>, keys: string[], defaultOpen = false): BriefingGroup {
  const picked = keys.reduce<Record<string, unknown>>((acc, sourceKey) => {
    if (sourceKey in source) acc[sourceKey] = source[sourceKey];
    return acc;
  }, {});
  return { key, title, value: picked, defaultOpen };
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
