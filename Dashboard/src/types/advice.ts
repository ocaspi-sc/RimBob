import type { IconRef } from './icons';

export type AdvicePriority = 'low' | 'medium' | 'high' | 'critical';

export type AdviceApplyKind = 'mark_harvest_area' | 'mark_hunt_area' | 'unforbid_things' | 'upsert_production_bill';

export interface AdviceApplyRect {
  x1: number;
  z1: number;
  x2: number;
  z2: number;
}

export interface AdviceThingApplyTarget {
  id: string;
  def: string;
  kind: string;
  source: string;
  position: {
    x: number;
    y: number;
    z: number;
  };
}

export interface AdviceActionApply {
  kind: AdviceApplyKind;
  label: string;
  target_summary: string;
  map_id: number;
  target_count: number;
  rect?: AdviceApplyRect | null;
  target_ids?: string[] | null;
  thing_ids?: string[] | null;
  thing_targets?: AdviceThingApplyTarget[] | null;
  workbench_building_id?: string | null;
  recipe_selector_key?: string | null;
  repeat_mode?: string | null;
}

export interface AdviceAction {
  kind: string;
  instruction: string;
  quantity?: number | null;
  owner?: string | null;
  work_type?: string | null;
  skill?: string | null;
  apply?: AdviceActionApply | null;
}

export interface MapCell {
  x: number;
  z: number;
}

export interface BlueprintAsset {
  role: string;
  def_name: string;
  stuff_def_name?: string | null;
  cell: MapCell;
  rotation: number;
}

export interface BlueprintGroup {
  label: string;
  map_id: number;
  assets: BlueprintAsset[];
}

export interface MaterialEstimate {
  def_name: string;
  count: number;
}

export interface AdviceOption {
  id: string;
  label: string;
  summary: string;
  blueprint_group: BlueprintGroup;
  est_materials: MaterialEstimate[];
  tradeoff_note?: string | null;
}

export interface AdviceApplyResponse {
  status: string;
  message: string;
  kind?: AdviceApplyKind | null;
  advice_id: string;
  action_index: number;
  readback?: unknown | null;
}

export type BuildingClass =
  | 'freezer'
  | 'wall'
  | 'door'
  | 'barricade'
  | 'embrasure'
  | 'power_generation'
  | 'battery'
  | 'conduit'
  | 'cooler'
  | 'heater'
  | 'vent'
  | 'bed'
  | 'production_bench'
  | 'research_bench'
  | 'multianalyzer'
  | 'stockpile'
  | 'shelf'
  | 'dumping_zone'
  | 'trade_beacon'
  | 'floor'
  | 'roof'
  | 'turret_platform'
  | string;

export type RoomClass =
  | 'freezer'
  | 'hospital'
  | 'kitchen'
  | 'butcher'
  | 'workshop'
  | 'research'
  | 'bedroom'
  | 'barracks'
  | 'prison'
  | 'recreation'
  | 'dining'
  | 'storage'
  | string;

export type CapacityMeasure = 'beds' | 'food_units' | 'work_slots' | 'storage_stacks' | 'occupants' | string;
export type AdjacencyRelation = 'near' | 'inside' | 'connected_to' | 'away_from' | string;
export type TemperatureBand = 'freezing' | 'cold' | 'room' | 'sterile_warm' | string;
export type Urgency = 'when_convenient' | 'soon' | 'before_deadline' | 'blocking_now' | string;
export type DeadlineKind = 'by_day' | 'by_season' | 'before_event' | string;

export interface CapacityNeed {
  measure: CapacityMeasure;
  amount?: number | null;
  unit?: string | null;
}

export interface AdjacencyHint {
  relation: AdjacencyRelation;
  target: string;
}

export interface PowerNeed {
  needs_power: boolean;
  approx_watts?: number | null;
}

export interface TempNeed {
  target_band: TemperatureBand;
  must_hold: boolean;
}

export interface MaterialHint {
  material: string;
  approx_qty?: number | null;
}

export interface Deadline {
  kind: DeadlineKind;
  value?: unknown | null;
}

export interface BuildingRequest {
  request: string;
  reason: string;
  target_class: BuildingClass;
  target_def?: string | null;
  room_class?: RoomClass | null;
  capacity_need?: CapacityNeed | null;
  adjacency?: AdjacencyHint[] | null;
  power?: PowerNeed | null;
  temperature?: TempNeed | null;
  materials_on_hand?: MaterialHint[] | null;
  urgency?: Urgency | null;
  deadline?: Deadline | null;
  quantity?: number | null;
  priority?: AdvicePriority | null;
  requested_from?: string | null;
}

export interface LaborRequest {
  request: string;
  reason: string;
  work_type?: string | null;
  skill?: string | null;
  quantity?: number | null;
  priority?: AdvicePriority | null;
  requested_from?: string | null;
}

export interface ItemRequest {
  request: string;
  reason: string;
  item_def?: string | null;
  quantity?: number | null;
  priority?: AdvicePriority | null;
  requested_from?: string | null;
}

export interface AttentionRequest {
  request: string;
  reason: string;
  priority?: AdvicePriority | null;
  requested_from?: string | null;
}

export type FlagSeverity = 'low' | 'medium' | 'high' | 'critical';

export interface AgentFlag {
  id: string;
  source_minister: string;
  severity: FlagSeverity;
  domain: string;
  summary: string;
  building_requests?: BuildingRequest[] | null;
  labor_requests?: LaborRequest[] | null;
  item_requests?: ItemRequest[] | null;
  attention?: AttentionRequest[] | null;
  detail?: string | null;
  expires_at?: string | null;
}

export interface SuggestedAction {
  kind: string;
  instruction: string;
  icon?: IconRef | null;
}

export type AdviceChainStepStatus = 'trigger' | 'action' | 'have' | 'available' | 'blocked';

export interface AdviceChainStep {
  key: string;
  label: string;
  detail: string;
  status: AdviceChainStepStatus;
}

export interface AdviceChainPath {
  name: string;
  steps: AdviceChainStep[];
}

export interface AdviceChainModel {
  paths: AdviceChainPath[];
}

export interface AdviceItem {
  id: string;
  minister: string;
  advice_type: string;
  priority: AdvicePriority;
  title: string;
  body: string;
  rationale: string;
  actions: AdviceAction[];
  options?: AdviceOption[] | null;
  suggested_actions?: SuggestedAction[];
  guide_citations: string[];
  issued_at: string;
  expires_at: string;
  issued_in_game_tick?: string | null;
  issued_game_tick?: number | null;
  expires_game_tick?: number | null;
}

export interface AdviceSnapshot {
  minister?: string | null;
  advice: AdviceItem[];
  state_summary?: string | null;
  state_summaries?: Record<string, string> | null;
  chain?: AdviceChainModel | null;
  chains?: Record<string, AdviceChainModel> | null;
  flags?: AgentFlag[] | null;
}
