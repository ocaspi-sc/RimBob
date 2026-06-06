import { useEffect, useState, type ReactNode } from 'react';
import {
  fetchSolverRequests,
  fetchZoneRequests,
  type WillieRequestBoardPayload,
  type WillieRequestRow,
  type WillieSolverOutputPayload,
  type WillieZoneRequestBoardPayload,
  type WillieZoneRequestRow,
} from '../../api/ministers';
import { iconUrlFor } from '../../api/icons';
import { displayMinisterName, type ScopeConfig } from '../../dashboard/scopes';
import { isScopeMinister } from '../../dashboard/selectors';
import { iconForField, iconForView } from '../../dashboard/semanticIcons';
import { useAsyncResource } from '../../hooks/useAsyncResource';
import type { AdviceOption, AdviceOptionReadiness, BuildingRequest, ZoneRequest } from '../../types/advice';
import type { MinisterTrace, SystemHealth } from '../../types/system';
import { EmptyState } from '../shared/EmptyState';
import { GameIcon } from '../shared/GameIcon';
import { IconizedText } from '../shared/IconizedText';
import { ResourceQuantity, ResourceQuantityList } from '../shared/ResourceQuantity';
import { SemanticLabel } from '../shared/SemanticIcon';
import { StatusPill, type PillTone } from '../shared/StatusPill';
import { BlueprintFootprintThumbnail } from './BlueprintFootprintThumbnail';
import { readinessTone } from './readiness';

export function MinisterRequestsView({
  scope,
  systemHealth,
}: {
  scope: ScopeConfig;
  systemHealth: SystemHealth | null;
}) {
  const latestTrace = findTrace(systemHealth, scope);
  const traceKey = latestTrace
    ? `${latestTrace.startedAt}|${latestTrace.completedAt ?? ''}|${latestTrace.status}`
    : 'no-trace';
  const board = useAsyncResource<WillieRequestBoardPayload | null>(
    signal => scope.key === 'willie'
      ? fetchSolverRequests(scope.key, signal)
      : Promise.resolve(null),
    [scope.key, traceKey],
  );
  const zoneBoard = useAsyncResource<WillieZoneRequestBoardPayload | null>(
    signal => scope.key === 'willie'
      ? fetchZoneRequests(scope.key, signal)
      : Promise.resolve(null),
    [scope.key, traceKey],
  );
  const rows = board.data?.requests ?? [];
  const zoneRows = zoneBoard.data?.requests ?? [];
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [selectedZoneKey, setSelectedZoneKey] = useState<string | null>(null);

  useEffect(() => {
    if (rows.length === 0) {
      setSelectedKey(null);
      return;
    }

    const keys = rows.map(row => requestKey(row.request));
    if (!selectedKey || !keys.includes(selectedKey)) {
      setSelectedKey(keys[0]);
    }
  }, [rows, selectedKey]);

  useEffect(() => {
    if (zoneRows.length === 0) {
      setSelectedZoneKey(null);
      return;
    }

    const keys = zoneRows.map(row => zoneRequestKey(row.request));
    if (!selectedZoneKey || !keys.includes(selectedZoneKey)) {
      setSelectedZoneKey(keys[0]);
    }
  }, [zoneRows, selectedZoneKey]);

  if (scope.key !== 'willie') {
    return <EmptyState code="REQUESTS NOT WIRED">Requests is a Willie-only construction view.</EmptyState>;
  }

  if (board.loading || zoneBoard.loading) {
    return <EmptyState code="REQUESTS">Loading current Willie requests.</EmptyState>;
  }

  if ((board.error || !board.data) && (zoneBoard.error || !zoneBoard.data)) {
    return (
      <EmptyState code="REQUESTS UNAVAILABLE">
        {board.error ?? zoneBoard.error ?? 'Willie requests endpoints returned no payload.'}
      </EmptyState>
    );
  }

  if (rows.length === 0 && zoneRows.length === 0) {
    return <EmptyState code="NO REQUESTS SEEN YET">Willie has not seen any inbound building or zone requests in this Host process.</EmptyState>;
  }

  const selected = rows.length > 0
    ? rows.find(row => requestKey(row.request) === selectedKey) ?? rows[0]
    : null;

  return (
    <div className="minister-view willie-requests-view">
      <header className="view-heading">
        <span className="eyebrow">{scope.displayLabel}</span>
        <h2><SemanticLabel icon={iconForView('requests')}><span>Requests</span></SemanticLabel></h2>
        <p>Current inbound building and zone requests. Building rows join to Placement Solver outcomes; zone rows join to grow-zone placement outcomes.</p>
      </header>

      {zoneRows.length > 0 && (
        <ZoneRequestsPanel
          onSelect={setSelectedZoneKey}
          rows={zoneRows}
          selectedKey={selectedZoneKey}
        />
      )}

      {selected ? (
        <div className="willie-requests-layout">
          <aside className="feature-filter-sidebar willie-request-sidebar" aria-label="Willie building requests">
            <div className="feature-filter-sidebar-heading">
              <strong>Building requests</strong>
              <small>{formatInteger(rows.length)}</small>
            </div>
            <div className="willie-request-list">
              {rows.map(row => (
                <RequestButton
                  active={requestKey(row.request) === requestKey(selected.request)}
                  key={requestKey(row.request)}
                  onClick={() => setSelectedKey(requestKey(row.request))}
                  row={row}
                />
              ))}
            </div>
          </aside>

          <RequestDetail row={selected} />
        </div>
      ) : (
        <EmptyState code="NO BUILDING REQUESTS">No inbound building requests are active; zone requests are listed above.</EmptyState>
      )}
    </div>
  );
}

function ZoneRequestsPanel({
  onSelect,
  rows,
  selectedKey,
}: {
  onSelect: (key: string) => void;
  rows: WillieZoneRequestRow[];
  selectedKey: string | null;
}) {
  const selected = rows.find(row => zoneRequestKey(row.request) === selectedKey) ?? rows[0];

  return (
    <div className="willie-requests-layout zone-request-layout">
      <aside className="feature-filter-sidebar willie-request-sidebar" aria-label="Willie zone requests">
        <div className="feature-filter-sidebar-heading">
          <strong>Zone requests</strong>
          <small>{formatInteger(rows.length)}</small>
        </div>
        <div className="willie-request-list">
          {rows.map(row => (
            <ZoneRequestButton
              active={zoneRequestKey(row.request) === zoneRequestKey(selected.request)}
              key={zoneRequestKey(row.request)}
              onClick={() => onSelect(zoneRequestKey(row.request))}
              row={row}
            />
          ))}
        </div>
      </aside>

      <ZoneRequestDetail row={selected} />
    </div>
  );
}

function ZoneRequestButton({
  active,
  onClick,
  row,
}: {
  active: boolean;
  onClick: () => void;
  row: WillieZoneRequestRow;
}) {
  const request = row.request;

  return (
    <button
      type="button"
      className={`willie-request-button ${active ? 'active' : ''}`}
      aria-current={active ? 'true' : undefined}
      onClick={onClick}
    >
      <span className="willie-request-button-body">
        <span className="eyebrow">{displayMinisterName(row.sourceMinister)}</span>
        <strong><IconizedText maxIcons={1} text={request.request} /></strong>
        <span className="willie-request-button-pills">
          <StatusPill tone={priorityTone(request.priority)}>{request.priority ?? 'priority unknown'}</StatusPill>
          <StatusPill tone={statusTone(row.status)}>{formatLabel(row.status)}</StatusPill>
        </span>
      </span>
    </button>
  );
}

function ZoneRequestDetail({ row }: { row: WillieZoneRequestRow }) {
  const request = row.request;

  return (
    <section className="request-detail-panel" aria-label={`${request.request} details`}>
      <header>
        <div>
          <span className="eyebrow">{displayMinisterName(row.sourceMinister)} -&gt; {displayMinisterName('Willie')}</span>
          <h3><IconizedText maxIcons={1} text={request.request} /></h3>
        </div>
        <StatusPill tone={statusTone(row.status)}>{formatLabel(row.status)}</StatusPill>
      </header>

      <div className="request-field-grid" aria-label="Zone request fields">
        <Field label="reason" value={<IconizedText maxIcons={2} text={request.reason} />} />
        <Field label="zone_class" value={formatLabel(request.zone_class)} />
        <Field label="plant_def" value={request.plant_def ?? '-'} />
        <Field label="tile_count" value={request.tile_count ?? '-'} />
        <Field label="adjacency" value={formatAdjacency(request.adjacency)} />
        <Field label="terrain" value={formatTerrain(request.terrain)} />
        <Field label="urgency" value={formatLabel(request.urgency)} />
        <Field label="deadline" value={formatDeadline(request.deadline)} />
        <Field label="priority" value={request.priority ?? '-'} />
        <Field label="requested_from" value={request.requested_from ?? '-'} />
        <Field label="source_minister" value={row.sourceMinister ?? '-'} />
      </div>

      <ZoneSolverOutcome row={row} />
    </section>
  );
}

function RequestButton({
  active,
  onClick,
  row,
}: {
  active: boolean;
  onClick: () => void;
  row: WillieRequestRow;
}) {
  const request = row.request;
  const outcome = outcomeMeta(row);

  return (
    <button
      type="button"
      className={`willie-request-button ${active ? 'active' : ''}`}
      aria-current={active ? 'true' : undefined}
      onClick={onClick}
    >
      <span className="willie-request-button-body">
        <span className="eyebrow">{displayMinisterName(row.sourceMinister)}</span>
        <strong><IconizedText maxIcons={1} text={request.request} /></strong>
        <span className="willie-request-button-pills">
          <StatusPill tone={priorityTone(request.priority)}>{request.priority ?? 'priority unknown'}</StatusPill>
          <StatusPill tone={outcome.tone}>{outcome.label}</StatusPill>
        </span>
      </span>
    </button>
  );
}

function RequestDetail({ row }: { row: WillieRequestRow }) {
  const request = row.request;
  const outcome = outcomeMeta(row);

  return (
    <section className="request-detail-panel" aria-label={`${request.request} details`}>
      <header>
        <div>
          <span className="eyebrow">{displayMinisterName(row.sourceMinister)} -&gt; {displayMinisterName('Willie')}</span>
          <h3><IconizedText maxIcons={1} text={request.request} /></h3>
        </div>
        <StatusPill tone={outcome.tone}>{outcome.label}</StatusPill>
      </header>

      <div className="request-field-grid" aria-label="Building request fields">
        <Field label="reason" value={<IconizedText maxIcons={2} text={request.reason} />} />
        <Field label="target_class" value={formatLabel(request.target_class)} />
        <Field label="target_def" value={request.target_def ?? '-'} />
        <Field label="room_class" value={formatLabel(request.room_class)} />
        <Field label="capacity_need" value={formatCapacity(request.capacity_need)} />
        <Field label="adjacency" value={formatAdjacency(request.adjacency)} />
        <Field label="power" value={formatPower(request.power)} />
        <Field label="temperature" value={formatTemperature(request.temperature)} />
        <Field label="materials_on_hand" value={formatMaterials(request.materials_on_hand)} />
        <Field label="urgency" value={formatLabel(request.urgency)} />
        <Field label="deadline" value={formatDeadline(request.deadline)} />
        <Field label="quantity" value={request.quantity ?? '-'} />
        <Field label="priority" value={request.priority ?? '-'} />
        <Field label="requested_from" value={request.requested_from ?? '-'} />
        <Field label="source_minister" value={row.sourceMinister ?? '-'} />
      </div>

      <SolverOutcome row={row} />
    </section>
  );
}

function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="request-field-row">
      <span><SemanticLabel icon={iconForField(label)}><span>{label}</span></SemanticLabel></span>
      <strong>{value}</strong>
    </div>
  );
}

function SolverOutcome({ row }: { row: WillieRequestRow }) {
  const output = row.output;

  if (!output) {
    return (
      <section className="request-solver-outcome">
        <header>
          <h4><SemanticLabel icon={iconForField('solver')}><span>Solver outcome</span></SemanticLabel></h4>
          <StatusPill tone="idle">awaiting</StatusPill>
        </header>
        <p>Awaiting solve - Willie has not run the Placement Solver for this request yet.</p>
      </section>
    );
  }

  const status = statusLabel(output);
  const options = output.status === 'options' ? row.options : [];

  return (
    <section className="request-solver-outcome">
      <header>
        <h4><SemanticLabel icon={iconForField('solver')}><span>Solver outcome</span></SemanticLabel></h4>
        <StatusPill tone={statusTone(output.status)}>{status.label}</StatusPill>
      </header>
      {status.note && <p>{status.note}</p>}
      {options.length > 0 ? (
        <div className="build-option-grid request-option-grid">
          {options.map(option => (
            <ReadOnlyOptionCard key={option.id} option={option} output={output} />
          ))}
        </div>
      ) : output.status === 'options' ? (
        <p>Placement Solver reported options, but no live option payloads are attached to this request.</p>
      ) : null}
    </section>
  );
}

function ZoneSolverOutcome({ row }: { row: WillieZoneRequestRow }) {
  const output = row.output;

  if (!output) {
    return (
      <section className="request-solver-outcome">
        <header>
          <h4><SemanticLabel icon={iconForField('zone_requests')}><span>Zone placement</span></SemanticLabel></h4>
          <StatusPill tone="idle">awaiting</StatusPill>
        </header>
        <p><IconizedText maxIcons={2} text={row.message} /></p>
      </section>
    );
  }

  const status = zoneStatusLabel(output);
  const options = output.status === 'options' ? row.options : [];

  return (
    <section className="request-solver-outcome">
      <header>
        <h4><SemanticLabel icon={iconForField('zone_requests')}><span>Zone placement</span></SemanticLabel></h4>
        <StatusPill tone={statusTone(output.status)}>{status.label}</StatusPill>
      </header>
      {status.note && <p><IconizedText maxIcons={2} text={status.note} /></p>}
      {options.length > 0 ? (
        <div className="build-option-grid request-option-grid">
          {options.map(option => (
            <ReadOnlyOptionCard key={option.id} option={option} output={output} />
          ))}
        </div>
      ) : output.status === 'options' ? (
        <p>Grow-zone solver reported options, but no live option payloads are attached to this request.</p>
      ) : null}
    </section>
  );
}

function ReadOnlyOptionCard({
  option,
  output,
}: {
  option: AdviceOption;
  output: WillieSolverOutputPayload;
}) {
  const isZoneOption = option.blueprint_group.assets.some(asset => asset.role === 'zone_cell');
  const optionIcon = iconForField(isZoneOption ? 'zone_requests' : 'place_blueprint');
  const readiness = option.readiness ?? readinessFromOutput(output);

  return (
    <article className="build-option-card request-option-card">
      <header>
        <GameIcon
          fallback={optionIcon?.fallback ?? 'BP'}
          label={optionIcon?.label ?? 'Blueprint option icon'}
          size="xs"
          src={iconUrlFor(optionIcon?.ref)}
        />
        <div>
          <span className="eyebrow">{isZoneOption ? 'read-only zone option' : 'read-only option'}</span>
          <h4>{option.label}</h4>
        </div>
        <StatusPill tone="info">{isZoneOption ? 'apply in advice' : 'diagnostic'}</StatusPill>
      </header>

      <BlueprintFootprintThumbnail group={option.blueprint_group} />
      <p><IconizedText maxIcons={2} text={option.summary} /></p>

      {readiness && (
        <div className="build-option-readiness" aria-label={`${option.label} readiness`}>
          <StatusPill tone={readinessTone(readiness.draftable)} title="Draftable">draft {readiness.draftable}</StatusPill>
          <StatusPill tone={readinessTone(readiness.placement_valid)} title="Placement validation">place {readiness.placement_valid}</StatusPill>
          <StatusPill tone={readinessTone(readiness.materials_ready)} title="Materials ready">mat {readiness.materials_ready}</StatusPill>
          <StatusPill tone={readinessTone(readiness.apply_ready)} title="Apply ready">apply {readiness.apply_ready}</StatusPill>
        </div>
      )}

      <div className="build-option-materials">
        {option.est_materials.length === 0 ? (
          <span>{isZoneOption ? 'No materials; zone write validates live cells on Apply.' : 'No material estimate.'}</span>
        ) : (
          option.est_materials.map(material => (
            <ResourceQuantity defName={material.def_name} key={`${option.id}-${material.def_name}`} quantity={material.count} />
          ))
        )}
      </div>

      {option.tradeoff_note && <blockquote><IconizedText maxIcons={1} text={option.tradeoff_note} /></blockquote>}
      {/* TODO: Keep Apply in Build Queue until this diagnostic view is deliberately promoted into an action surface. */}
    </article>
  );
}

function findTrace(systemHealth: SystemHealth | null, scope: ScopeConfig): MinisterTrace | null {
  return systemHealth?.traces.find(trace => isScopeMinister(trace.minister, scope)) ?? null;
}

function outcomeMeta(row: WillieRequestRow): { label: string; tone: PillTone } {
  if (!row.output) return { label: 'awaiting', tone: 'idle' };
  if (row.output.status === 'options') return { label: 'options', tone: 'ok' };
  if (row.output.status === 'no_fit') return { label: 'no-fit', tone: 'warn' };
  if (row.output.status === 'error') return { label: 'error', tone: 'error' };
  if (row.output.status === 'offline') return { label: 'offline', tone: 'warn' };
  return { label: row.output.status.replace(/_/g, ' '), tone: 'idle' };
}

function statusLabel(output: WillieSolverOutputPayload): { label: string; note?: string } {
  if (output.status === 'options') {
    return {
      label: 'options',
      note: 'Validated options are attached below. Apply remains in the Build Queue tab.',
    };
  }

  if (output.status === 'no_fit') {
    return {
      label: `no-fit: ${formatLabel(output.noFit ?? 'unknown')}`,
      note: 'No validated option reached this request.',
    };
  }

  if (output.status === 'error') {
    return {
      label: `error: ${output.errorType ?? 'unknown'}`,
      note: output.errorMessage ?? 'Placement solver failed before returning a trace.',
    };
  }

  if (output.status === 'offline') {
    return {
      label: `offline: ${output.errorType ?? 'unknown'}`,
      note: output.errorMessage ?? 'Live map validation was unavailable.',
    };
  }

  return { label: output.status.replace(/_/g, ' ') };
}

function zoneStatusLabel(output: WillieSolverOutputPayload): { label: string; note?: string } {
  if (output.status === 'options') {
    return {
      label: 'options',
      note: 'Grow-zone coordinate options are attached below. Apply lives on Willie advice and validates live cells before writing.',
    };
  }

  if (output.status === 'no_fit') {
    return {
      label: `no-fit: ${formatLabel(output.noFit ?? 'unknown')}`,
      note: 'No compact growable zone rectangle reached this request.',
    };
  }

  if (output.status === 'error') {
    return {
      label: `error: ${output.errorType ?? 'unknown'}`,
      note: output.errorMessage ?? 'Grow-zone solver failed before returning a trace.',
    };
  }

  return { label: output.status.replace(/_/g, ' ') };
}

function statusTone(status: string): PillTone {
  if (status === 'options') return 'ok';
  if (status === 'no_fit' || status === 'offline') return 'warn';
  if (status === 'error') return 'error';
  return 'idle';
}

function readinessFromOutput(output: WillieSolverOutputPayload): AdviceOptionReadiness | null {
  if (!output.draftable || !output.placementValid || !output.materialsReady || !output.applyReady) {
    return null;
  }

  return {
    draftable: readinessWire(output.draftable),
    placement_valid: readinessWire(output.placementValid),
    materials_ready: readinessWire(output.materialsReady),
    apply_ready: readinessWire(output.applyReady),
  };
}

function readinessWire(value: string): string {
  return value.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase();
}

function requestKey(request: BuildingRequest): string {
  return [
    request.target_class,
    request.target_def,
    request.room_class,
    request.request,
  ].map(keyPart).join('|');
}

function zoneRequestKey(request: ZoneRequest): string {
  return [
    request.zone_class,
    request.plant_def,
    request.tile_count === null || request.tile_count === undefined ? null : String(request.tile_count),
    request.request,
  ].map(keyPart).join('|');
}

function keyPart(value: string | null | undefined): string {
  return value && value.trim() ? value.trim().toLowerCase() : '-';
}

function priorityTone(priority: string | null | undefined): PillTone {
  const normalized = (priority ?? '').toLowerCase();
  if (normalized === 'critical' || normalized === 'high') return 'error';
  if (normalized === 'medium') return 'warn';
  if (normalized === 'low') return 'info';
  return 'idle';
}

function formatCapacity(capacity: BuildingRequest['capacity_need']): string {
  if (!capacity) return '-';
  const amount = capacity.amount === null || capacity.amount === undefined
    ? ''
    : ` ${formatNumber(capacity.amount)}`;
  const unit = capacity.unit ? ` ${capacity.unit}` : '';
  return `${formatLabel(capacity.measure)}${amount}${unit}`;
}

function formatAdjacency(adjacency: BuildingRequest['adjacency']): string {
  if (!adjacency || adjacency.length === 0) return '-';
  return adjacency
    .map(hint => `${formatLabel(hint.relation)} ${hint.target}`)
    .join(', ');
}

function formatTerrain(terrain: ZoneRequest['terrain']): string {
  if (!terrain) return '-';
  const parts = [
    terrain.must_support_growing ? 'must_support_growing=true' : 'must_support_growing=false',
    terrain.preferred_fertility === null || terrain.preferred_fertility === undefined
      ? null
      : `preferred_fertility=${formatNumber(terrain.preferred_fertility)}`,
    terrain.preferred_terrain_defs && terrain.preferred_terrain_defs.length > 0
      ? `preferred_terrain_defs=${terrain.preferred_terrain_defs.join(', ')}`
      : null,
  ].filter((value): value is string => Boolean(value));
  return parts.join(', ');
}

function formatPower(power: BuildingRequest['power']): string {
  if (!power) return '-';
  return power.approx_watts === null || power.approx_watts === undefined
    ? `needs_power=${power.needs_power}`
    : `needs_power=${power.needs_power}, approx_watts=${formatInteger(power.approx_watts)}`;
}

function formatTemperature(temperature: BuildingRequest['temperature']): string {
  if (!temperature) return '-';
  return `${formatLabel(temperature.target_band)}, must_hold=${temperature.must_hold}`;
}

function formatMaterials(materials: BuildingRequest['materials_on_hand']): ReactNode {
  if (!materials || materials.length === 0) return '-';
  return (
    <ResourceQuantityList
      items={materials.map(material => ({
        approx: material.approx_qty !== null && material.approx_qty !== undefined,
        defName: material.material,
        quantity: material.approx_qty,
      }))}
    />
  );
}

function formatDeadline(deadline: BuildingRequest['deadline']): string {
  if (!deadline) return '-';
  if (deadline.value === null || deadline.value === undefined) return formatLabel(deadline.kind);
  return `${formatLabel(deadline.kind)} ${formatUnknown(deadline.value)}`;
}

function formatUnknown(value: unknown): string {
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return String(value);
  }

  return JSON.stringify(value);
}

function formatLabel(value: string | null | undefined): string {
  if (!value) return '-';
  if (value === 'buildable_region') return 'Buildable region / Home area';
  return value
    .replace(/_/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/\b\w/g, char => char.toUpperCase());
}

function formatInteger(value: number): string {
  return Math.round(value).toLocaleString();
}

function formatNumber(value: number): string {
  return Number.isInteger(value) ? formatInteger(value) : value.toLocaleString();
}
