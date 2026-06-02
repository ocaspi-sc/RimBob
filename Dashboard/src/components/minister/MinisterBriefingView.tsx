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
  const willieBriefing = scope.key === 'willie' && isRecord(briefing.data) ? briefing.data : null;
  const isWillieBriefing = willieBriefing !== null;
  const welfareBriefing = scope.key === 'welfare' && isRecord(briefing.data) ? briefing.data : null;
  const isWelfareBriefing = welfareBriefing !== null;

  return (
    <div className="minister-view briefing-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.label}</span>
        <h2><SemanticLabel icon={iconForView('briefing')} size="sm"><span>Briefing</span></SemanticLabel></h2>
        <p>Readable source data behind this minister's reasoning.</p>
      </header>

      {coverageRows.length > 0 && !isWillieBriefing && !isWelfareBriefing && (
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
      {isWelfareBriefing && (
        <WelfareBriefingHud briefing={welfareBriefing} />
      )}
      {isWillieBriefing && (
        <WillieBriefingHud briefing={willieBriefing} />
      )}

      {isWillieBriefing || isWelfareBriefing ? (
        <RawBriefing groups={groups} />
      ) : (
        groups.map(group => (
          <DisclosureSection
            key={group.key}
            title={<SemanticLabel icon={iconForSection(group.key)}><span>{group.title}</span></SemanticLabel>}
            defaultOpen={group.defaultOpen}
            meta={summarizeValue(group.value)}
          >
            <JsonTree value={group.value} />
          </DisclosureSection>
        ))
      )}
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
      pickGroup('sleep', 'Sleep', briefing, ['sleep']),
      pickGroup('recreation', 'Recreation', briefing, ['recreation']),
      pickGroup('thought_digest', 'Thought Digest', briefing, ['thoughtDigest']),
      pickGroup('data_coverage', 'Data coverage', briefing, ['dataCoverage']),
      { key: 'raw_remaining_fields', title: 'Raw remaining fields', value: omitKeys(briefing, [
        'briefingVersion', 'gameTick', 'colonistCount', 'mood', 'worstPawns', 'needLows', 'rooms', 'sleep',
        'recreation', 'thoughtDigest', 'dataCoverage',
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

type HudTone = 'ok' | 'warn' | 'error' | 'neutral';

interface WillieConcernTile {
  detail: string;
  key: string;
  label: string;
  tone: HudTone;
  value: string;
}

interface WelfareConcernTile {
  detail: string;
  key: string;
  label: string;
  tone: HudTone;
  value: string;
}

interface WillieCoverageRow {
  key: string;
  value: 'available' | 'missing';
}

function WelfareBriefingHud({ briefing }: { briefing: Record<string, unknown> }) {
  const mood = recordAt(briefing, 'mood');
  const sleep = recordAt(briefing, 'sleep');
  const recreation = recordAt(briefing, 'recreation');
  const thoughtDigest = recordAt(briefing, 'thoughtDigest');
  const rooms = recordAt(briefing, 'rooms');
  const coverage = recordAt(briefing, 'dataCoverage');
  const worstPawns = arrayAt(briefing, 'worstPawns');
  const needLows = arrayAt(briefing, 'needLows');
  const thoughtGroups = arrayAt(thoughtDigest, 'byCategory');
  const coverageRows: WillieCoverageRow[] = coverage
    ? Object.entries(coverage).map(([key, value]) => ({ key, value: value === true ? 'available' : 'missing' }))
    : [];
  const breakRiskCount = numberAt(mood, 'breakRiskCount') ?? 0;
  const stressedCount = numberAt(mood, 'stressedCount') ?? 0;
  const contentCount = numberAt(mood, 'contentCount') ?? 0;
  const colonistCount = numberAt(briefing, 'colonistCount') ?? 0;
  const bedDeficit = numberAt(sleep, 'bedDeficit') ?? 0;
  const unroofedBedroomCount = numberAt(sleep, 'unroofedBedroomCount') ?? 0;
  const joyLowCount = numberAt(recreation, 'joyLowCount') ?? 0;
  const hasRecreationSource = booleanAt(recreation, 'hasRecreationSource');
  const comfortBeautyGroup = thoughtGroup(thoughtGroups, 'comfort_beauty');
  const concernTiles = buildWelfareConcernTiles({
    bedDeficit,
    breakRiskCount,
    comfortBeautyGroup,
    joyLowCount,
    recreation,
    sleep,
    unroofedBedroomCount,
  });

  return (
    <section className="welfare-briefing-hud" aria-label="Welfare briefing HUD">
      <div className="willie-hud-strip">
        <div className="willie-concern-strip welfare-concern-strip" aria-label="Welfare concern severity">
          {concernTiles.map(tile => (
            <article key={tile.key} className={`willie-concern-tile ${tile.tone}`}>
              <header>
                <SemanticLabel icon={iconForField(tile.key)}><span>{tile.label}</span></SemanticLabel>
                <span>{humanize(tile.tone)}</span>
              </header>
              <strong>{tile.value}</strong>
              <small>{tile.detail}</small>
            </article>
          ))}
        </div>
        <WillieCoverageBars rows={coverageRows} label="Welfare data coverage" />
      </div>

      <div className="metric-grid welfare-briefing-metrics">
        <MetricCard
          label={metricLabel('mood', 'Average mood')}
          value={formatNullablePercent(numberAt(mood, 'averageMood'))}
          note={`${formatInteger(contentCount)} content / ${formatInteger(stressedCount)} stressed / ${formatInteger(breakRiskCount)} break-risk`}
          tone={breakRiskCount > 0 ? 'error' : stressedCount > 0 ? 'warn' : 'ok'}
        />
        <MetricCard
          label={metricLabel('shelter_floor', 'Beds')}
          value={`${formatInteger(numberAt(sleep, 'bedCount'))}/${formatInteger(numberAt(sleep, 'colonistCount') ?? colonistCount)}`}
          note={`${formatInteger(bedDeficit)} deficit / ${formatInteger(unroofedBedroomCount)} unroofed rooms`}
          tone={bedDeficit > 0 || unroofedBedroomCount > 0 ? 'warn' : 'ok'}
        />
        <MetricCard
          label={metricLabel('recreation_gap', 'Recreation')}
          value={`${formatInteger(joyLowCount)} low joy`}
          note={hasRecreationSource === null ? 'source coverage unavailable' : hasRecreationSource ? 'source visible' : 'no source visible'}
          tone={joyLowCount > 0 ? 'warn' : 'ok'}
        />
        <MetricCard
          label={metricLabel('comfort_beauty', 'Thought groups')}
          value={formatInteger(thoughtGroups.length)}
          note={thoughtGroups.length > 0 ? formatThoughtGroup(thoughtGroups[0]) : 'no negative thought categories'}
        />
      </div>

      <div className="welfare-briefing-panels">
        <article className="willie-readout-panel">
          <header>
            <h3><SemanticLabel icon={iconForField('mood')}><span>Mood And Needs</span></SemanticLabel></h3>
            <span className={breakRiskCount > 0 ? 'status-text error' : stressedCount > 0 ? 'status-text warn' : 'status-text ok'}>
              {breakRiskCount > 0 ? 'break risk' : stressedCount > 0 ? 'stressed' : 'stable'}
            </span>
          </header>
          <div className="welfare-pawn-list">
            {worstPawns.length > 0 ? worstPawns.slice(0, 8).map((pawn, index) => (
              <WelfarePawnRow key={stringAt(pawn, 'id') ?? `pawn-${index}`} pawn={pawn} />
            )) : (
              <span>No pawn mood rows are available.</span>
            )}
          </div>
        </article>

        <article className="willie-readout-panel">
          <header>
            <h3><SemanticLabel icon={iconForField('shelter_floor')}><span>Sleep Shelter</span></SemanticLabel></h3>
            <span className={bedDeficit > 0 || unroofedBedroomCount > 0 ? 'status-text warn' : 'status-text ok'}>
              {bedDeficit > 0 ? 'bed deficit' : unroofedBedroomCount > 0 ? 'roof gap' : 'covered'}
            </span>
          </header>
          <div className="willie-fact-grid">
            <Fact label="Bedrooms" value={formatInteger(numberAt(rooms, 'bedroomCount'))} />
            <Fact label="Beds" value={formatInteger(numberAt(sleep, 'bedCount'))} />
            <Fact label="Deficit" value={formatInteger(bedDeficit)} />
            <Fact label="Unroofed rooms" value={formatInteger(unroofedBedroomCount)} />
          </div>
        </article>

        <article className="willie-readout-panel">
          <header>
            <h3><SemanticLabel icon={iconForField('recreation_gap')}><span>Recreation And Comfort</span></SemanticLabel></h3>
            <span className={joyLowCount > 0 || comfortBeautyGroup ? 'status-text warn' : 'status-text ok'}>
              {joyLowCount > 0 || comfortBeautyGroup ? 'pressure' : 'clear'}
            </span>
          </header>
          <div className="willie-fact-grid">
            <Fact label="Low joy" value={formatInteger(joyLowCount)} />
            <Fact label="Rec rooms" value={formatInteger(numberAt(recreation, 'recreationRoomCount'))} />
            <Fact label="Joy buildings" value={formatInteger(numberAt(recreation, 'joySourceBuildingCount'))} />
            <Fact label="Need lows" value={formatInteger(needLows.length)} />
          </div>
          <DynamicTable
            rows={thoughtGroups}
            preferredColumns={['category', 'pawnCount', 'worstOffset', 'exampleLabel']}
            emptyMessage="No negative thought groups are available."
          />
        </article>
      </div>
    </section>
  );
}

function WillieBriefingHud({ briefing }: { briefing: Record<string, unknown> }) {
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
  const coverageRows: WillieCoverageRow[] = coverage
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
  const concernTiles = buildWillieConcernTiles({
    baseLayout,
    blockedCount,
    disallowedCount,
    fireRisk,
    material,
    missingMaterials,
    netW,
    pendingBuildCount,
    roomCounts,
    roomsMissing,
    storage,
    thermal,
  });

  return (
    <section className="willie-briefing-hud" aria-label="Willie briefing HUD">
      <div className="willie-hud-strip">
        <div className="willie-concern-strip" aria-label="Willie concern severity">
          {concernTiles.map(tile => (
            <article key={tile.key} className={`willie-concern-tile ${tile.tone}`}>
              <header>
                <SemanticLabel icon={iconForField(tile.key)}><span>{tile.label}</span></SemanticLabel>
                <span>{humanize(tile.tone)}</span>
              </header>
              <strong>{tile.value}</strong>
              <small>{tile.detail}</small>
            </article>
          ))}
        </div>
        <WillieCoverageBars rows={coverageRows} />
      </div>

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
            <h3><SemanticLabel icon={iconForField('base_layout')}><span>Layout And Fire Risk</span></SemanticLabel></h3>
            <span className={(numberAt(fireRisk, 'woodStructureCount') ?? 0) > 0 ? 'status-text warn' : 'status-text ok'}>
              {(numberAt(fireRisk, 'woodStructureCount') ?? 0) > 0 ? 'wood risk' : 'clear'}
            </span>
          </header>
          <div className="willie-fact-grid">
            <Fact label="Rooms" value={formatInteger(numberAt(baseLayout, 'roomCount'))} />
            <Fact label="Buildings" value={formatInteger(numberAt(baseLayout, 'buildingCount'))} />
            <Fact label="Wood structures" value={formatInteger(numberAt(fireRisk, 'woodStructureCount'))} />
          </div>
        </article>
      </div>
    </section>
  );
}

function WillieCoverageBars({ rows, label = 'Willie data coverage' }: { rows: WillieCoverageRow[]; label?: string }) {
  if (rows.length === 0) return null;

  const available = rows.filter(row => row.value === 'available').length;

  return (
    <aside className="willie-coverage-bars" aria-label={label}>
      <header>
        <SemanticLabel icon={iconForField('data_coverage')}><span>Data Coverage</span></SemanticLabel>
        <strong>{available}/{rows.length}</strong>
      </header>
      <div>
        {rows.map(row => (
          <span key={row.key} className={`willie-coverage-row ${row.value}`}>
            <SemanticLabel icon={iconForField(row.key)}><span>{humanize(row.key)}</span></SemanticLabel>
            <i aria-hidden="true"><b style={{ width: row.value === 'available' ? '100%' : '18%' }} /></i>
            <CoverageBadge state={row.value} />
          </span>
        ))}
      </div>
    </aside>
  );
}

function RawBriefing({ groups }: { groups: BriefingGroup[] }) {
  return (
    <DisclosureSection
      title={<SemanticLabel icon={iconForSection('raw_payload')}><span>Raw briefing</span></SemanticLabel>}
      meta={`${groups.length} grouped sections`}
    >
      <div className="raw-briefing-groups">
        {groups.map(group => (
          <article className="raw-briefing-group" key={group.key}>
            <h4><SemanticLabel icon={iconForSection(group.key)}><span>{group.title}</span></SemanticLabel></h4>
            <JsonTree expandDepth={1} value={group.value} />
          </article>
        ))}
      </div>
    </DisclosureSection>
  );
}

function WelfarePawnRow({ pawn }: { pawn: unknown }) {
  if (!isRecord(pawn)) return null;
  const name = stringAt(pawn, 'name') ?? stringAt(pawn, 'id') ?? 'unknown';
  const thought = firstThoughtLabel(pawn);

  return (
    <div className="welfare-pawn-row">
      <strong>{name}</strong>
      <span>Mood {formatNullablePercent(numberAt(pawn, 'mood'))}</span>
      <span>Sleep {formatNullablePercent(numberAt(pawn, 'sleep'))}</span>
      <span>Comfort {formatNullablePercent(numberAt(pawn, 'comfort'))}</span>
      <span>Beauty {formatNullablePercent(numberAt(pawn, 'beauty'))}</span>
      <span>Joy {formatNullablePercent(numberAt(pawn, 'joy'))}</span>
      <small>{thought ?? 'no negative thought detail'}</small>
    </div>
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

function buildWillieConcernTiles({
  baseLayout,
  blockedCount,
  disallowedCount,
  fireRisk,
  material,
  missingMaterials,
  netW,
  pendingBuildCount,
  roomCounts,
  roomsMissing,
  storage,
  thermal,
}: {
  baseLayout: Record<string, unknown> | null;
  blockedCount: number;
  disallowedCount: number;
  fireRisk: Record<string, unknown> | null;
  material: Record<string, unknown> | null;
  missingMaterials: unknown[];
  netW: number | null;
  pendingBuildCount: number;
  roomCounts: Record<string, unknown> | null;
  roomsMissing: string[];
  storage: Record<string, unknown> | null;
  thermal: Record<string, unknown> | null;
}): WillieConcernTile[] {
  const stockpileZones = numberAt(storage, 'stockpileZones');
  const stockpileCells = numberAt(storage, 'stockpileCells');
  const coolerCount = numberAt(thermal, 'coolerCount');
  const freezerAnchorCount = numberAt(thermal, 'freezerAnchorCount');
  const woodStructureCount = numberAt(fireRisk, 'woodStructureCount');
  const roomCount = numberAt(baseLayout, 'roomCount');
  const buildingCount = numberAt(baseLayout, 'buildingCount');
  const knownRooms = roomCounts ? Object.keys(roomCounts).length : 0;

  return [
    {
      key: 'power_stability',
      label: 'Power Stability',
      value: netW === null ? 'unknown' : netW < 0 ? 'deficit' : 'stable',
      detail: netW === null ? 'No power summary available.' : `${formatWatts(netW)} net output.`,
      tone: netW === null ? 'neutral' : netW < 0 ? 'error' : 'ok',
    },
    {
      key: 'thermal_control',
      label: 'Thermal Control',
      value: `${formatInteger(coolerCount)} coolers`,
      detail: `${formatInteger(freezerAnchorCount)} freezer anchors; heaters tracked in detail below.`,
      tone: coolerCount === null ? 'neutral' : freezerAnchorCount !== null && freezerAnchorCount > 0 && coolerCount <= 0 ? 'warn' : 'ok',
    },
    {
      key: 'functional_rooms',
      label: 'Functional Rooms',
      value: roomsMissing.length > 0 ? `${roomsMissing.length} missing` : 'covered',
      detail: knownRooms > 0 ? `${knownRooms} room classes currently counted.` : 'No room-class counts available yet.',
      tone: roomsMissing.length > 0 ? 'warn' : 'ok',
    },
    {
      key: 'storage_placement',
      label: 'Storage Placement',
      value: `${formatInteger(stockpileZones)} zones`,
      detail: `${formatInteger(stockpileCells)} stockpile cells visible.`,
      tone: stockpileZones === null && stockpileCells === null ? 'neutral' : (stockpileZones ?? 0) <= 0 && (stockpileCells ?? 0) <= 0 ? 'warn' : 'ok',
    },
    {
      key: 'material_bottleneck',
      label: 'Material Bottleneck',
      value: missingMaterials.length > 0 ? `${missingMaterials.length} gaps` : 'clear',
      detail: `${formatInteger(numberAt(material, 'blockedCount'))} blocked by material summary.`,
      tone: missingMaterials.length > 0 ? 'warn' : 'ok',
    },
    {
      key: 'fire_risk',
      label: 'Fire Risk',
      value: `${formatInteger(woodStructureCount)} wood`,
      detail: 'Wood structure count from the current map summary.',
      tone: woodStructureCount === null ? 'neutral' : woodStructureCount > 0 ? 'warn' : 'ok',
    },
    {
      key: 'stalled_builds',
      label: 'Stalled Builds',
      value: `${formatInteger(blockedCount)} blocked`,
      detail: `${formatInteger(pendingBuildCount)} pending; ${formatInteger(disallowedCount)} disallowed.`,
      tone: blockedCount > 0 || disallowedCount > 0 ? 'warn' : pendingBuildCount > 0 ? 'neutral' : 'ok',
    },
    {
      key: 'base_layout',
      label: 'Base Layout',
      value: `${formatInteger(roomCount)} rooms`,
      detail: `${formatInteger(buildingCount)} buildings in the current aggregate.`,
      tone: roomCount === null && buildingCount === null ? 'neutral' : 'ok',
    },
  ];
}

function buildWelfareConcernTiles({
  bedDeficit,
  breakRiskCount,
  comfortBeautyGroup,
  joyLowCount,
  recreation,
  sleep,
  unroofedBedroomCount,
}: {
  bedDeficit: number;
  breakRiskCount: number;
  comfortBeautyGroup: Record<string, unknown> | null;
  joyLowCount: number;
  recreation: Record<string, unknown> | null;
  sleep: Record<string, unknown> | null;
  unroofedBedroomCount: number;
}): WelfareConcernTile[] {
  const hasRecreationSource = booleanAt(recreation, 'hasRecreationSource');
  return [
    {
      key: 'break_risk',
      label: 'Break Risk',
      value: `${formatInteger(breakRiskCount)} at risk`,
      detail: breakRiskCount > 0 ? 'Immediate mood triage is needed.' : 'No pawn is below the break threshold.',
      tone: breakRiskCount > 0 ? 'error' : 'ok',
    },
    {
      key: 'shelter_floor',
      label: 'Shelter Floor',
      value: `${formatInteger(numberAt(sleep, 'bedCount'))}/${formatInteger(numberAt(sleep, 'colonistCount'))} beds`,
      detail: `${formatInteger(bedDeficit)} bed deficit; ${formatInteger(unroofedBedroomCount)} unroofed sleeping rooms.`,
      tone: bedDeficit > 0 || unroofedBedroomCount > 0 ? 'warn' : 'ok',
    },
    {
      key: 'recreation_gap',
      label: 'Recreation Gap',
      value: `${formatInteger(joyLowCount)} low joy`,
      detail: hasRecreationSource === null ? 'Building coverage unavailable.' : hasRecreationSource ? 'A recreation source is visible.' : 'No recreation source is visible.',
      tone: joyLowCount > 0 ? 'warn' : 'ok',
    },
    {
      key: 'comfort_beauty',
      label: 'Comfort Beauty',
      value: comfortBeautyGroup ? `${formatInteger(numberAt(comfortBeautyGroup, 'pawnCount'))} thoughts` : 'clear',
      detail: comfortBeautyGroup ? formatThoughtGroup(comfortBeautyGroup) : 'No comfort/beauty thought group.',
      tone: comfortBeautyGroup ? 'warn' : 'ok',
    },
  ];
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

function stringAt(source: unknown, key: string): string | null {
  if (!isRecord(source)) return null;
  const value = source[key];
  return typeof value === 'string' ? value : null;
}

function booleanAt(source: Record<string, unknown> | null, key: string): boolean | null {
  if (!source) return null;
  const value = source[key];
  return typeof value === 'boolean' ? value : null;
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

function formatNullablePercent(value: number | null): string {
  return value === null ? 'n/a' : formatPercent(value);
}

function formatThoughtGroup(value: unknown): string {
  if (!isRecord(value)) return 'unknown thought group';
  const category = typeof value.category === 'string' ? humanize(value.category) : 'Thought';
  const pawns = numberFromUnknown(value.pawnCount);
  const example = typeof value.exampleLabel === 'string' ? value.exampleLabel : null;
  return `${category}${pawns === null ? '' : ` / ${formatInteger(pawns)} pawn${pawns === 1 ? '' : 's'}`}${example ? ` / ${example}` : ''}`;
}

function thoughtGroup(groups: unknown[], category: string): Record<string, unknown> | null {
  return groups
    .filter(isRecord)
    .find(group => typeof group.category === 'string' && group.category.toLowerCase() === category) ?? null;
}

function firstThoughtLabel(pawn: Record<string, unknown>): string | null {
  const thoughts = arrayAt(pawn, 'topNegativeThoughts');
  const first = thoughts.find(isRecord);
  if (!first) return null;
  return stringAt(first, 'label') ?? stringAt(first, 'defName');
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
