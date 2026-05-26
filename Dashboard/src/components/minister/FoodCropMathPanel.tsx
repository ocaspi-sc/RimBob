import { fetchFoodCropMath, type FoodCropCandidate } from '../../api/ministers';
import { itemIconUrl } from '../../api/icons';
import { iconForField, iconForSection } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import { DisclosureSection } from '../shared/DisclosureSection';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { MetricCard } from '../shared/MetricCard';
import { SemanticLabel } from '../shared/SemanticIcon';
import { StatusPill, type PillTone } from '../shared/StatusPill';

export function FoodCropMathPanel() {
  const cropMath = useAsyncResource(fetchFoodCropMath, []);

  if (cropMath.loading) {
    return (
      <DisclosureSection title={<SemanticLabel icon={iconForSection('crop_math')}><span>Crop math</span></SemanticLabel>} defaultOpen meta="loading">
        <EmptyState code="CROP MATH">Loading crop candidates.</EmptyState>
      </DisclosureSection>
    );
  }

  if (cropMath.error || cropMath.data === null) {
    return (
      <DisclosureSection title={<SemanticLabel icon={iconForSection('crop_math')}><span>Crop math</span></SemanticLabel>} defaultOpen meta="failed">
        <EmptyState code="CROP MATH FAILED">{cropMath.error ?? 'No crop math payload returned.'}</EmptyState>
      </DisclosureSection>
    );
  }

  const data = cropMath.data;
  const best = data.bestCandidate ?? data.candidates.find(candidate => candidate.fitsSeason) ?? data.candidates[0] ?? null;
  const fitCount = data.candidates.filter(candidate => candidate.fitsSeason).length;

  return (
    <DisclosureSection title={<SemanticLabel icon={iconForSection('crop_math')}><span>Crop math</span></SemanticLabel>} defaultOpen meta={`${data.candidates.length} candidates`}>
      <div className="crop-math-panel">
        <div className="metric-grid compact crop-math-summary">
          <MetricCard
            label={<SemanticLabel icon={best ? { fallback: best.cropDef.slice(0, 2).toUpperCase(), label: `${best.cropDef} icon`, ref: { kind: 'item', id: best.cropDef } } : iconForField('crop_def')}><span>Best candidate</span></SemanticLabel>}
            value={best?.label ?? 'none'}
            note={best ? best.cropDef : undefined}
            tone={best?.fitsSeason ? 'ok' : 'warn'}
          />
          <MetricCard
            label={<SemanticLabel icon={iconForField('estimated_days_of_food')}><span>Food buffer</span></SemanticLabel>}
            value={formatDays(data.estimatedDaysOfFood)}
            note={`${data.colonistCount} colonists`}
            tone={data.estimatedDaysOfFood === null ? 'warn' : data.estimatedDaysOfFood < 7 ? 'error' : 'ok'}
          />
          <MetricCard
            label={<SemanticLabel icon={iconForField('winter_window')}><span>Winter window</span></SemanticLabel>}
            value={data.season.daysToWinter === null ? 'unknown' : `${formatNumber(data.season.daysToWinter)} days`}
            note={data.season.currentSeason}
            tone={data.season.daysToWinter === null ? 'warn' : 'neutral'}
          />
          <MetricCard
            label={<SemanticLabel icon={iconForField('terrain_fertility')}><span>Terrain fertility</span></SemanticLabel>}
            value={formatMaybeNumber(data.growingTerrain.bestFertility)}
            note={data.growingTerrain.hasTerrain ? `${data.growingTerrain.growableCells.toLocaleString()} growable cells` : 'not exposed'}
            tone={data.growingTerrain.hasTerrain ? 'ok' : 'warn'}
          />
        </div>

        <div className="crop-math-context">
          <span>briefing v{data.briefingVersion}</span>
          <span>tick {data.gameTick.toLocaleString()}</span>
          <span>{data.date.label}</span>
          <span>{data.date.totalDays.toFixed(1)} total days</span>
          <span>{data.nutritionSource}</span>
          <span>{fitCount}/{data.candidates.length} fit season</span>
        </div>

        {data.candidates.length === 0 ? (
          <EmptyState code="NO CROP CANDIDATES">Food crop math returned no candidate rows.</EmptyState>
        ) : (
          <div className="dense-table crop-math-table">
            <div className="dense-row header">
              <SemanticLabel icon={iconForField('crop_def')}><span>Crop</span></SemanticLabel>
              <span>Tiles</span>
              <span>Grow days</span>
              <SemanticLabel icon={iconForField('estimated_days_of_food')}><span>Days added</span></SemanticLabel>
              <SemanticLabel icon={iconForField('season')}><span>Season</span></SemanticLabel>
              <SemanticLabel icon={iconForField('terrain_fertility')}><span>Fertility</span></SemanticLabel>
              <SemanticLabel icon={iconForField('classification_confidence')}><span>Confidence</span></SemanticLabel>
              <SemanticLabel icon={iconForField('storage')}><span>Storage</span></SemanticLabel>
              <span>Score</span>
              <span>Reason</span>
            </div>
            {data.candidates.map(candidate => (
              <div className="dense-row" key={candidate.cropDef}>
                <span className="crop-name">
                  <GameIcon
                    fallback={candidate.cropDef.slice(0, 2).toUpperCase()}
                    label={`${candidate.cropDef} icon`}
                    size="xs"
                    src={itemIconUrl(candidate.cropDef)}
                  />
                  <span>
                    <strong>{candidate.label}</strong>
                    <code>{candidate.cropDef}</code>
                  </span>
                </span>
                <span>{candidate.tiles}</span>
                <span>{formatNumber(candidate.growDays)}</span>
                <span>{formatNumber(candidate.projectedDaysAdded)}</span>
                <span>
                  <StatusPill tone={candidateTone(candidate)}>
                    {candidate.fitsSeason ? 'fits' : 'misses'}
                  </StatusPill>
                  {candidate.daysToWinterMargin !== null && (
                    <small>{formatNumber(candidate.daysToWinterMargin)}d margin</small>
                  )}
                </span>
                <span>{formatMaybeNumber(candidate.terrainFertility)}</span>
                <span>{formatPercent(candidate.classificationConfidence)}</span>
                <span>{formatPercent(candidate.storageMultiplier)}</span>
                <span>{formatNumber(candidate.score, 2)}</span>
                <span>{candidate.reason}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </DisclosureSection>
  );
}

function candidateTone(candidate: FoodCropCandidate): PillTone {
  return candidate.fitsSeason ? 'ok' : 'warn';
}

function formatDays(value: number | null): string {
  return value === null ? 'unknown' : `${formatNumber(value)} days`;
}

function formatMaybeNumber(value: number | null): string {
  return value === null ? 'unknown' : formatNumber(value);
}

function formatNumber(value: number, digits = 1): string {
  return value.toLocaleString(undefined, {
    maximumFractionDigits: digits,
    minimumFractionDigits: value % 1 === 0 ? 0 : Math.min(digits, 1),
  });
}

function formatPercent(value: number): string {
  return `${Math.round(value * 100)}%`;
}
