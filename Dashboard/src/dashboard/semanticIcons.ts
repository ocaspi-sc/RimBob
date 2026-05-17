import type { IconRef } from '../types/icons';
import type { JsonValue } from '../components/shared/JsonTree';

export interface SemanticIconSpec {
  fallback: string;
  label: string;
  ref: IconRef;
}

type JsonRecord = { [key: string]: JsonValue };

function item(id: string, label: string, fallback = id.slice(0, 2).toUpperCase()): SemanticIconSpec {
  return { fallback, label, ref: { kind: 'item', id } };
}

function terrain(id: string, label: string, fallback = id.slice(0, 2).toUpperCase()): SemanticIconSpec {
  return { fallback, label, ref: { kind: 'terrain', id } };
}

function pawn(id: string, label: string, fallback = 'P'): SemanticIconSpec {
  return { fallback, label, ref: { kind: 'pawn', id } };
}

const common = {
  advice: item('CommsConsole', 'Advice icon', 'AD'),
  analytics: item('SimpleResearchBench', 'Analytics icon', 'AN'),
  briefing: item('TextBook', 'Briefing icon', 'BR'),
  component: item('ComponentIndustrial', 'System component icon', 'SY'),
  construction: item('Wall', 'Construction icon', 'CO'),
  data: item('ComponentIndustrial', 'Data coverage icon', 'DA'),
  defense: item('Gun_Revolver', 'Defense icon', 'DE'),
  economy: item('Silver', 'Economy icon', 'EC'),
  food: item('MealSimple', 'Food icon', 'FO'),
  harvest: item('Plant_Rice', 'Forage harvest icon', 'HA'),
  info: item('TextBook', 'Info icon', 'IN'),
  industry: item('Steel', 'Industry icon', 'ID'),
  kitchen: item('MealSimple', 'Kitchen icon', 'KI'),
  labor: item('Steel', 'Labor icon', 'LA'),
  logs: item('CommsConsole', 'Raw output icon', 'LO'),
  mayor: item('CommsConsole', 'Mayor icon', 'MY'),
  medical: item('MedicineIndustrial', 'Medical icon', 'ME'),
  people: item('Bed', 'People icon', 'PE'),
  power: item('ComponentIndustrial', 'Power icon', 'PW'),
  prompt: item('SimpleResearchBench', 'Prompt icon', 'PR'),
  rag: item('TextBook', 'RAG icon', 'RG'),
  raw: item('ComponentIndustrial', 'Raw payload icon', 'RW'),
  research: item('SimpleResearchBench', 'Research icon', 'RE'),
  rules: item('Steel', 'Rules icon', 'RU'),
  season: item('Plant_Rice', 'Season icon', 'SE'),
  storage: item('WoodLog', 'Storage icon', 'ST'),
  threat: item('Gun_Revolver', 'Threat icon', 'TH'),
  weather: item('WoodLog', 'Weather icon', 'WE'),
  welfare: item('Bed', 'Welfare icon', 'WF'),
};

const scopeIcons: Record<string, SemanticIconSpec> = {
  system: common.component,
  info: common.info,
  analytics: common.analytics,
  mayor: common.mayor,
  food: common.food,
  construction: common.construction,
  defense: common.defense,
  welfare: common.welfare,
  medical: common.medical,
  research: common.research,
  industry: common.industry,
  economy: common.economy,
  chief_of_staff: item('OrbitalTradeBeacon', 'Chief of Staff icon', 'CS'),
};

const viewIcons: Record<string, SemanticIconSpec> = {
  prompt: common.prompt,
  briefing: common.briefing,
  rag: common.rag,
  rules: common.rules,
  raw_llm: common.logs,
  advice: common.advice,
};

const sectionIcons: Record<string, SemanticIconSpec> = {
  overview: common.briefing,
  people: common.people,
  food_status: common.food,
  food_and_resources: common.food,
  crops: item('Plant_Rice', 'Crops icon', 'CR'),
  wild_harvest: common.harvest,
  skills_and_labor: common.labor,
  infrastructure_and_storage: common.storage,
  infrastructure: common.construction,
  kitchen_and_butchery: common.kitchen,
  data_coverage: common.data,
  recent_incidents: common.threat,
  welfare_and_threat: common.welfare,
  environment: common.weather,
  research: common.research,
  raw_remaining_fields: common.raw,
  raw_payload: common.raw,
  recent_scope_events: common.logs,
  active_advice_emitted: common.advice,
  assisted_apply: common.advice,
  crop_math: item('Plant_Rice', 'Crop math icon', 'CM'),
  colonists: common.people,
  resources: common.storage,
  state_of_the_union: common.mayor,
  what_changed: common.logs,
  closed_items: common.advice,
  long_term_goals: common.research,
  cabinet_direction: common.mayor,
  tests: common.data,
  runtime: common.component,
  host: common.component,
  info: common.info,
  rimapi: common.data,
  icon_cache: common.storage,
  endpoint_coverage: common.data,
  recent_events: common.logs,
  logs: common.logs,
};

const fieldIcons: Record<string, SemanticIconSpec> = {
  active_raid: common.threat,
  active_threat: common.threat,
  advice: common.advice,
  advice_type: common.advice,
  agenda: common.mayor,
  average_mood: common.welfare,
  bytes: common.storage,
  briefing_version: common.briefing,
  buildings: common.construction,
  cabinet: common.mayor,
  capture_metadata: common.logs,
  captured_at: common.logs,
  categories: common.data,
  category: common.data,
  classification_confidence: common.data,
  client: common.data,
  client_methods: common.data,
  colonist_count: common.people,
  colonists: common.people,
  completed_at: common.rules,
  connections: common.data,
  confidence: common.data,
  crop_breakdown: item('Plant_Rice', 'Crop breakdown icon', 'CB'),
  crop_candidates: item('Plant_Rice', 'Crop candidates icon', 'CC'),
  crop_def: item('Plant_Rice', 'Crop definition icon', 'CD'),
  crop_zone_summaries: item('Plant_Rice', 'Crop zones icon', 'CZ'),
  data_coverage: common.data,
  date: common.season,
  days_to_winter: common.season,
  deferred_writes: common.threat,
  emitted_advice: common.advice,
  endpoint: common.data,
  error: common.threat,
  events: common.logs,
  estimated_days_of_food: common.food,
  fallback_nutrition: common.food,
  file: common.logs,
  files: common.logs,
  flags: common.threat,
  food: common.food,
  food_units: common.food,
  game_tick: common.briefing,
  guide_citations: common.rag,
  growing_terrain: item('Plant_Rice', 'Growing terrain icon', 'GT'),
  harvest_nutrition: common.harvest,
  health: common.medical,
  host: common.component,
  hunger: common.food,
  icon_cache: common.storage,
  id: common.data,
  infrastructure: common.construction,
  issued_at: common.advice,
  kind: common.data,
  kinds: common.data,
  kitchen: common.kitchen,
  last_event: common.logs,
  live: common.data,
  llm: common.prompt,
  logs: common.logs,
  materials: common.storage,
  mayor: common.mayor,
  meals_count: common.food,
  medical: common.medical,
  message: common.logs,
  method: common.data,
  mood: common.welfare,
  note: common.logs,
  nutrition_source: common.food,
  owner: common.mayor,
  path: common.rules,
  power: common.power,
  priority: common.advice,
  raw_food_count: common.harvest,
  raw_response: common.raw,
  ready_to_harvest: common.harvest,
  recent_food_incidents: common.threat,
  reconnects: common.data,
  replay: common.logs,
  request: common.advice,
  resource_requests: common.storage,
  reported_nutrition: common.food,
  research: common.research,
  resources: common.storage,
  rimapi: common.data,
  rule_fired: common.rules,
  rules: common.rules,
  season: common.season,
  server_events: common.logs,
  severity: common.threat,
  size: common.storage,
  skills: common.labor,
  source: common.data,
  status: common.component,
  actions: common.advice,
  stockpile_cells: common.storage,
  storage: common.storage,
  summary: common.advice,
  suggested_actions: common.advice,
  system_prompt: common.prompt,
  terrain_fertility: item('Plant_Rice', 'Terrain fertility icon', 'TF'),
  tests: common.data,
  threat: common.threat,
  top_k: common.rag,
  trigger: common.rules,
  updated: common.logs,
  user_prompt: common.prompt,
  warm_result: common.storage,
  weather: common.weather,
  wealth: common.economy,
  wild_animal_count: item('Hare', 'Wild animal icon', 'WA'),
  wild_harvest_candidates: common.harvest,
  wild_harvest_clusters: common.harvest,
  winter_window: common.season,
  work_type: common.labor,
};

export function iconForScope(key: string): SemanticIconSpec | undefined {
  return scopeIcons[key];
}

export function iconForView(key: string): SemanticIconSpec | undefined {
  return viewIcons[key];
}

export function iconForSection(key: string): SemanticIconSpec | undefined {
  return sectionIcons[normalizeKey(key)] ?? iconForField(key);
}

export function iconForField(pathOrKey: string): SemanticIconSpec | undefined {
  const normalized = normalizeKey(pathOrKey);
  const segments = normalized.split('.').filter(Boolean);

  for (let i = 0; i < segments.length; i += 1) {
    const suffix = segments.slice(i).join('.');
    if (fieldIcons[suffix]) return fieldIcons[suffix];
  }

  const last = segments[segments.length - 1] ?? normalized;
  return fieldIcons[last] ?? fallbackIconForKey(last);
}

export function iconForFieldValue(fieldKey: string | undefined, value: JsonValue): SemanticIconSpec | undefined {
  if (!fieldKey || typeof value !== 'string' || value.trim() === '') return undefined;

  const normalized = normalizeKey(fieldKey);
  const id = value.trim();
  if (normalized.endsWith('terrain') || normalized.endsWith('terraindef') || normalized.endsWith('terrain_def')) {
    return terrain(id, `${id} terrain icon`, id.slice(0, 2).toUpperCase());
  }

  if (
    normalized.endsWith('def') ||
    normalized.endsWith('defname') ||
    normalized.endsWith('def_name') ||
    normalized.includes('crop') ||
    normalized.includes('item') ||
    normalized.includes('resource') ||
    normalized.includes('medicine') ||
    normalized.includes('weapon')
  ) {
    return item(id, `${id} icon`, id.slice(0, 2).toUpperCase());
  }

  return undefined;
}

export function iconForRecord(record: JsonRecord): SemanticIconSpec | undefined {
  const pawnId = readString(record, ['pawnId', 'pawn_id', 'id']);
  const name = readString(record, ['name', 'label']);
  if (pawnId && looksLikePawnRecord(record)) {
    return pawn(pawnId, `${name ?? pawnId} portrait`, initials(name ?? pawnId));
  }

  const terrainDef = readString(record, ['terrainDef', 'terrain_def', 'terrain']);
  if (terrainDef) {
    return terrain(terrainDef, `${terrainDef} terrain icon`, terrainDef.slice(0, 2).toUpperCase());
  }

  const itemDef = readString(record, ['cropDef', 'crop_def', 'itemDef', 'item_def', 'thingDef', 'thing_def', 'defName', 'def_name', 'def']);
  if (itemDef) {
    return item(itemDef, `${itemDef} icon`, itemDef.slice(0, 2).toUpperCase());
  }

  return undefined;
}

function fallbackIconForKey(key: string): SemanticIconSpec | undefined {
  if (key.includes('food') || key.includes('meal') || key.includes('nutrition')) return common.food;
  if (key.includes('crop') || key.includes('plant')) return item('Plant_Rice', 'Crop icon', 'CR');
  if (key.includes('harvest') || key.includes('wild')) return common.harvest;
  if (key.includes('skill') || key.includes('labor') || key.includes('work')) return common.labor;
  if (key.includes('kitchen') || key.includes('cook') || key.includes('butcher')) return common.kitchen;
  if (key.includes('storage') || key.includes('stockpile') || key.includes('resource')) return common.storage;
  if (key.includes('threat') || key.includes('raid') || key.includes('incident')) return common.threat;
  if (key.includes('medical') || key.includes('health') || key.includes('downed')) return common.medical;
  if (key.includes('mood') || key.includes('welfare')) return common.welfare;
  if (key.includes('power') || key.includes('battery')) return common.power;
  if (key.includes('research')) return common.research;
  if (key.includes('wealth') || key.includes('silver')) return common.economy;
  if (key.includes('weather') || key.includes('temperature')) return common.weather;
  if (key.includes('season') || key.includes('winter') || key.includes('date')) return common.season;
  if (key.includes('log') || key.includes('trace') || key.includes('raw')) return common.logs;
  if (key.includes('status') || key.includes('coverage') || key.includes('version')) return common.data;
  return undefined;
}

function normalizeKey(value: string): string {
  return value
    .replace(/\[(\d+)\]/g, '.$1')
    .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
    .replace(/[^a-zA-Z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .toLowerCase();
}

function readString(record: JsonRecord, keys: string[]): string | null {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.trim() !== '') return value;
    if (typeof value === 'number') return String(value);
  }
  return null;
}

function looksLikePawnRecord(record: JsonRecord): boolean {
  return 'mood' in record || 'health' in record || 'hunger' in record || 'currentJob' in record || 'current_job' in record;
}

function initials(value: string): string {
  return value.trim().slice(0, 1).toUpperCase() || 'P';
}
