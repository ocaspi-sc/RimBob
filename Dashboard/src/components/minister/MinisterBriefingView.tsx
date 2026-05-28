import { fetchBriefing } from '../../api/ministers';
import type { ScopeConfig } from '../../dashboard/scopes';
import { iconForField, iconForSection, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { CoverageBadge } from '../shared/DataCoverage';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { DynamicTable } from '../shared/Inspector';
import { JsonTree, summarizeValue } from '../shared/JsonTree';
import { MetricCard } from '../shared/MetricCard';
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
      {scope.key === 'willie' && isRecord(briefing.data) && (
        <WillieBriefingReadout briefing={briefing.data} />
      )}

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

function WillieBriefingReadout({ briefing }: { briefing: Record<string, unknown> }) {
  const power = recordAt(briefing, 'powerStability');
  const thermal = recordAt(briefing, 'thermalControl');
  const rooms = recordAt(briefing, 'functionalRooms');
  const storage = recordAt(briefing, 'storagePlacement');
  const material = recordAt(briefing, 'materialBottleneck');
  const stalled = recordAt(briefing, 'stalledBuilds');
  const baseLayout = recordAt(briefing, 'baseLayout');
  const fireRisk = recordAt(briefing, 'fireRisk');
  const coverage = recordAt(briefing, 'dataCoverage');
  const anchorInventory = recordAt(briefing, 'anchorInventory');
  const constructionBacklog = recordAt(briefing, 'constructionBacklog');
  const anchors = arrayAt(anchorInventory, 'anchors');
  const backlogGroups = arrayAt(constructionBacklog, 'groups');
  const missingMaterials = arrayAt(material, 'missingMaterials');
  const roomCounts = recordAt(rooms, 'roomCountsByClass');
  const coverageRows = coverage
    ? Object.entries(coverage).map(([key, value]) => ({ key, value: value === true ? 'available' : 'missing' }))
    : [];

  const netW = numberAt(power, 'netW');
  const storedWd = numberAt(power, 'storedWd');
  const capacityWd = numberAt(power, 'capacityWd');
  const reserveRatio = storedWd !== null && capacityWd !== null && capacityWd > 0
    ? storedWd / capacityWd
    : null;
  const pendingBuildCount = numberAt(stalled, 'pendingBuildCount') ?? 0;
  const blockedCount = numberAt(stalled, 'blockedCount') ?? numberAt(material, 'blockedCount') ?? 0;
  const disallowedCount = numberAt(stalled, 'disallowedCount') ?? numberAt(material, 'disallowedCount') ?? 0;
  const roomsMissing = missingExpectedRooms(roomCounts);

  return (
    <section className="willie-briefing-readout" aria-label="Willie briefing readout">
      <div className="metric-grid willie-briefing-metrics">
        <MetricCard
          label={metricLabel('power_stability', 'Net power')}
          value={formatWatts(netW)}
          note={`${formatWatts(numberAt(power, 'productionW'))} production / ${formatWatts(numberAt(power, 'consumptionW'))} load`}
          tone={netW !== null && netW < 0 ? 'error' : 'ok'}
        />
        <MetricCard
          label={metricLabel('briefing_version', 'Briefing')}
          value={`v${formatInteger(numberAt(briefing, 'briefingVersion'))}`}
          note={`tick ${formatInteger(numberAt(briefing, 'gameTick'))}`}
        />
        <MetricCard
          label={metricLabel('build_queue', 'Build queue')}
          value={`${formatInteger(pendingBuildCount)} pending`}
          note={`${formatInteger(blockedCount)} blocked / ${formatInteger(disallowedCount)} disallowed`}
          tone={blockedCount > 0 ? 'warn' : 'neutral'}
        />
        <MetricCard
          label={metricLabel('storage_placement', 'Storage footprint')}
          value={`${formatInteger(numberAt(storage, 'stockpileZones'))} zones`}
          note={`${formatInteger(numberAt(storage, 'stockpileCells'))} stockpile cells`}
        />
        <MetricCard
          label={metricLabel('functional_rooms', 'Room anchors')}
          value={formatInteger(anchors.length)}
          note={roomsMissing.length > 0 ? `Missing ${roomsMissing.join(', ')}` : 'Kitchen, hospital, storage evidence present when anchors exist'}
          tone={roomsMissing.length > 0 ? 'warn' : 'ok'}
        />
        <MetricCard
          label={metricLabel('thermal_control', 'Thermal')}
          value={`${formatInteger(numberAt(thermal, 'coolerCount'))} coolers`}
          note={`${formatInteger(numberAt(thermal, 'freezerAnchorCount'))} freezer anchors / ${formatInteger(numberAt(thermal, 'heaterCount'))} heaters`}
        />
      </div>

      <div className="willie-briefing-panels">
        <article className="willie-readout-panel">
          <header>
            <h3><SemanticLabel icon={iconForField('power_stability')}><span>Power Stability</span></SemanticLabel></h3>
            <span className={netW !== null && netW < 0 ? 'status-text error' : 'status-text ok'}>
              {netW !== null && netW < 0 ? 'deficit' : 'stable'}
            </span>
          </header>
          <div className="willie-fact-grid">
            <Fact label="Generators" value={formatInteger(numberAt(power, 'generatorCount'))} />
            <Fact label="Batteries" value={formatInteger(numberAt(power, 'batteryCount'))} />
            <Fact label="Battery reserve" value={reserveRatio === null ? 'n/a' : `${formatPercent(reserveRatio)} (${formatWd(storedWd)} / ${formatWd(capacityWd)})`} />
          </div>
        </article>

        <article className="willie-readout-panel">
          <header>
            <h3><SemanticLabel icon={iconForField('material_bottleneck')}><span>Build Queue And Materials</span></SemanticLabel></h3>
            <span className={missingMaterials.length > 0 || blockedCount > 0 ? 'status-text warn' : 'status-text ok'}>
              {missingMaterials.length > 0 ? `${missingMaterials.length} material gaps` : 'no material gaps'}
            </span>
          </header>
          <div className="willie-material-list">
            {missingMaterials.length > 0 ? (
              missingMaterials.slice(0, 6).map((item, index) => <span key={`missing-${index}`}>{formatMaterial(item)}</span>)
            ) : (
              <span>No missing materials reported by the current construction backlog.</span>
            )}
          </div>
          <DynamicTable
            rows={backlogGroups}
            preferredColumns={['kind', 'defName', 'stuffDefName', 'allowed', 'count', 'blockedCount', 'disallowedCount', 'totalWorkLeft']}
            emptyMessage="No construction backlog groups are visible."
          />
        </article>

        <article className="willie-readout-panel">
          <header>
            <h3><SemanticLabel icon={iconForField('functional_rooms')}><span>Functional Rooms And Anchors</span></SemanticLabel></h3>
            <span className={roomsMissing.length > 0 ? 'status-text warn' : 'status-text ok'}>
              {roomsMissing.length > 0 ? `${roomsMissing.length} missing` : 'covered'}
            </span>
          </header>
          <div className="willie-room-counts">
            {roomCounts && Object.keys(roomCounts).length > 0 ? (
              Object.entries(roomCounts).map(([key, value]) => <span key={key}>{humanize(key)} {formatInteger(numberFromUnknown(value))}</span>)
            ) : (
              <span>No room-class counts available yet.</span>
            )}
          </div>
          <DynamicTable
            rows={anchors}
            preferredColumns={['class', 'roleLabel', 'cellsCount', 'roomId', 'centroid', 'regionId']}
            emptyMessage="No classified room anchors are available."
          />
        </article>

        <article className="willie-readout-panel">
          <header>
            <h3><SemanticLabel icon={iconForField('base_layout')}><span>Layout, Risk, And Coverage</span></SemanticLabel></h3>
            <span className={coverageRows.some(row => row.value === 'missing') ? 'status-text warn' : 'status-text ok'}>
              {coverageRows.filter(row => row.value === 'available').length}/{coverageRows.length} signals
            </span>
          </header>
          <div className="willie-fact-grid">
            <Fact label="Rooms" value={formatInteger(numberAt(baseLayout, 'roomCount'))} />
            <Fact label="Buildings" value={formatInteger(numberAt(baseLayout, 'buildingCount'))} />
            <Fact label="Wood structures" value={formatInteger(numberAt(fireRisk, 'woodStructureCount'))} />
          </div>
          <div className="willie-coverage-grid">
            {coverageRows.map(row => (
              <span key={row.key} className={row.value}>
                <SemanticLabel icon={iconForField(row.key)}><code>{row.key}</code></SemanticLabel>
                <CoverageBadge state={row.value === 'available' ? 'available' : 'missing'} />
              </span>
            ))}
          </div>
        </article>
      </div>
    </section>
  );
}

function extractCoverage(briefing: unknown): Array<{ key: string; label: string; ok: boolean }> {
  if (!isRecord(briefing) || !isRecord(briefing.dataCoverage)) return [];
  return Object.entries(briefing.dataCoverage).map(([key, value]) => ({
    key,
    label: humanize(key),
    ok: value === true,
  }));
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <span>
      <small>{label}</small>
      <strong>{value}</strong>
    </span>
  );
}

function metricLabel(iconKey: string, label: string) {
  return <SemanticLabel icon={iconForField(iconKey)}><span>{label}</span></SemanticLabel>;
}

function recordAt(source: Record<string, unknown> | null, key: string): Record<string, unknown> | null {
  if (!source) return null;
  const value = source[key];
  return isRecord(value) ? value : null;
}

function arrayAt(source: Record<string, unknown> | null, key: string): unknown[] {
  if (!source) return [];
  const value = source[key];
  return Array.isArray(value) ? value : [];
}

function numberAt(source: Record<string, unknown> | null, key: string): number | null {
  if (!source) return null;
  return numberFromUnknown(source[key]);
}

function numberFromUnknown(value: unknown): number | null {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  return null;
}

function formatInteger(value: number | null): string {
  if (value === null) return 'n/a';
  return Math.round(value).toLocaleString();
}

function formatWatts(value: number | null): string {
  if (value === null) return 'n/a';
  const sign = value > 0 ? '+' : '';
  return `${sign}${Math.round(value).toLocaleString()} W`;
}

function formatWd(value: number | null): string {
  if (value === null) return 'n/a';
  return `${Math.round(value).toLocaleString()} Wd`;
}

function formatPercent(value: number): string {
  return `${Math.round(value * 100)}%`;
}

function formatMaterial(value: unknown): string {
  if (!isRecord(value)) return 'unknown material';
  const defName = typeof value.defName === 'string' ? value.defName : 'unknown';
  const count = numberFromUnknown(value.count) ?? numberFromUnknown(value.missing) ?? numberFromUnknown(value.required);
  return count === null ? defName : `${formatInteger(count)} ${defName}`;
}

function missingExpectedRooms(roomCounts: Record<string, unknown> | null): string[] {
  if (!roomCounts) return ['kitchen', 'hospital', 'storage'];
  const normalized = new Map(Object.entries(roomCounts).map(([key, value]) => [key.toLocaleLowerCase(), value]));
  return ['kitchen', 'hospital', 'storage'].filter(room => (numberFromUnknown(normalized.get(room)) ?? 0) <= 0);
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
