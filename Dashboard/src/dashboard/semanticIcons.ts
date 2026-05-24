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
  devBlog: item('TextBook', 'Dev Blog icon', 'DB'),
  economy: item('Silver', 'Economy icon', 'EC'),
  food: item('MealSimple', 'Food icon', 'FO'),
  harvest: item('Plant_Rice', 'Forage harvest icon', 'HA'),
  info: item('TextBook', 'Info icon', 'IN'),
  industry: item('ElectricSmithy', 'Industry icon', 'ID'),
  infographics: item('SimpleResearchBench', 'Infographics icon', 'IG'),
  kitchen: item('ElectricStove', 'Kitchen icon', 'KI'),
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
  dev_blog: common.devBlog,
  mayor: common.mayor,
  food: common.food,
  construction: common.construction,
  defense: item('Barricade', 'Defense icon', 'DE'),
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
  infographics: common.infographics,
  advice: common.advice,
  runtime: common.component,
  connectivity: common.data,
  storage: common.storage,
  coverage: common.data,
  events: common.logs,
  overview: common.info,
  glossary: common.info,
  contracts: common.rules,
  data_sources: common.storage,
  session: common.analytics,
  colony: common.mayor,
  sse: common.logs,
  candidates: common.research,
  timeline: common.logs,
  features: common.devBlog,
  churn: common.data,
  commits: common.devBlog,
  topics: common.analytics,
  suggestions: common.advice,
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
  active_advice: common.advice,
  active_advice_emitted: common.advice,
  advice: common.advice,
  advice_item: common.advice,
  concern: common.advice,
  agenda: common.mayor,
  agenda_priorities: common.mayor,
  additions: common.data,
  attention: common.advice,
  animal_positions: item('Meat_Squirrel', 'Animal positions icon', 'AP'),
  average_mood: common.welfare,
  bytes: common.storage,
  briefing_version: common.briefing,
  building_requests: common.construction,
  buildings: common.construction,
  cabinet: common.mayor,
  capture_metadata: common.logs,
  captured_at: common.logs,
  categories: common.data,
  category: common.data,
  churn: common.data,
  commit: common.devBlog,
  commits: common.devBlog,
  classification_confidence: common.data,
  client: common.data,
  client_methods: common.data,
  colonist_count: common.people,
  colonists: common.people,
  completed_at: common.rules,
  connections: common.data,
  confidence: common.data,
  construction: common.construction,
  conditions: common.data,
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
  food_buffer: common.food,
  food_units: common.food,
  game_tick: common.briefing,
  guide_citations: common.rag,
  growing_terrain: item('Plant_Rice', 'Growing terrain icon', 'GT'),
  has_animal_positions: item('Meat_Squirrel', 'Animal positions icon', 'AP'),
  has_building_positions: common.construction,
  has_item_food_classification: common.food,
  has_live_state: common.component,
  has_plant_positions: item('Plant_Rice', 'Plant positions icon', 'PP'),
  has_terrain_fertility: item('Plant_Rice', 'Terrain fertility icon', 'TF'),
  has_trade_availability: item('OrbitalTradeBeacon', 'Trade availability icon', 'TA'),
  has_work_priorities: common.labor,
  has_zone_cells: item('Plant_Rice', 'Zone cells icon', 'ZC'),
  harvest_nutrition: common.harvest,
  health: common.medical,
  host: common.component,
  hunger: common.food,
  icon_cache: common.storage,
  id: common.data,
  infrastructure: common.construction,
  item_requests: common.storage,
  issued_at: common.advice,
  kind: common.data,
  kinds: common.data,
  kitchen: common.kitchen,
  kitchen_storage: common.kitchen,
  labor_requests: common.labor,
  latest_event: common.logs,
  last_event: common.logs,
  live: common.data,
  llm: common.prompt,
  llm_escalation: common.prompt,
  logs: common.logs,
  mark_harvest: common.harvest,
  mark_hunt: item('Gun_Revolver', 'Hunt icon', 'HU'),
  materials: common.storage,
  mayor: common.mayor,
  meals_count: common.food,
  medical: common.medical,
  message: common.logs,
  minister: common.mayor,
  method: common.data,
  mood: common.welfare,
  note: common.logs,
  nutrition_source: common.food,
  outcome: common.rules,
  owner: common.mayor,
  output_action: common.advice,
  path: common.rules,
  power: common.power,
  power_net: common.power,
  priority: common.advice,
  production_bill: common.kitchen,
  quantity: common.storage,
  raw_food_count: common.harvest,
  raw_response: common.raw,
  ready_to_harvest: common.harvest,
  recent_food_incidents: common.threat,
  reconnects: common.data,
  replay: common.logs,
  replay_corpus: common.logs,
  request: common.advice,
  reason: common.rules,
  reported_nutrition: common.food,
  research: common.research,
  resources: common.storage,
  rimapi: common.data,
  rule: common.rules,
  rule_fired: common.rules,
  rules: common.rules,
  season: common.season,
  server_events: common.logs,
  severity: common.threat,
  size: common.storage,
  skills: common.labor,
  sse: common.logs,
  sse_state: common.logs,
  source: common.data,
  status: common.component,
  actions: common.advice,
  set_stockpile_zone: common.food,
  selected_rule: common.rules,
  stockpile_cells: common.storage,
  storage: common.storage,
  summary: common.advice,
  suggested_actions: common.advice,
  system_prompt: common.prompt,
  terrain_fertility: item('Plant_Rice', 'Terrain fertility icon', 'TF'),
  tests: common.data,
  threat: common.threat,
  top_k: common.rag,
  trace_count: common.logs,
  trigger: common.rules,
  updated: common.logs,
  user_prompt: common.prompt,
  warm_result: common.storage,
  weather: common.weather,
  wealth: common.economy,
  wealth_colonist: common.economy,
  wild_animal_count: item('Meat_Squirrel', 'Wild animal icon', 'WA'),
  wild_harvest_candidates: common.harvest,
  wild_harvest_clusters: common.harvest,
  winter_window: common.season,
  work_type: common.labor,
};

const ruleOutcomeIcons: Record<string, SemanticIconSpec> = {
  escalated: common.prompt,
  matched: common.rules,
  not_matched: common.data,
  selected: common.advice,
  suppressed: common.threat,
};

const agendaCategoryIcons: Record<string, SemanticIconSpec> = {
  construction: common.construction,
  defense: scopeIcons.defense,
  food: common.food,
  research: common.research,
  treasury: common.economy,
  welfare: common.welfare,
};

const infoTagIcons: Record<string, SemanticIconSpec> = {
  architecture: common.component,
  cabinet: common.advice,
  code: common.component,
  construction: common.construction,
  coordination: common.threat,
  cost: common.economy,
  food: common.food,
  knowledge: common.rag,
  labor: common.labor,
  live_feed: common.logs,
  mayor: common.mayor,
  medical: common.medical,
  operations: common.component,
  pawn: common.people,
  prompts: common.prompt,
  project: common.component,
  refine: common.logs,
  runtime: common.rules,
  safety: common.threat,
  signals: common.analytics,
  state: common.briefing,
  threat: common.threat,
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

export function iconForAgendaCategory(key: string): SemanticIconSpec | undefined {
  return agendaCategoryIcons[normalizeKey(key)] ?? iconForField(key);
}

export function iconForAgendaPriority(text: string): SemanticIconSpec | undefined {
  return iconForDomainText(text);
}

export function iconForStateSummaryLine(label: string | null, detail: string): SemanticIconSpec | undefined {
  const labelKey = normalizeKey(label ?? '');
  const text = `${labelKey} ${normalizeKey(detail)}`;

  if (labelKey === 'stores') return common.food;
  if (labelKey === 'crops') return text.includes('corn')
    ? item('Plant_Corn', 'Corn crop icon', 'CO')
    : text.includes('potato')
      ? item('Plant_Potato', 'Potato crop icon', 'PO')
      : item('Plant_Rice', 'Rice crop icon', 'RI');
  if (labelKey === 'acquisition') {
    if (text.includes('hare') || text.includes('hunt')) return item('Gun_Revolver', 'Hunt target icon', 'HU');
    if (text.includes('berry')) return item('Plant_Berry', 'Berry plant icon', 'BE');
    return common.harvest;
  }
  if (labelKey === 'kitchen_storage') {
    if (text.includes('cooking') || text.includes('meal')) return common.kitchen;
    if (text.includes('cooler') || text.includes('freezer')) return item('Cooler', 'Cooler icon', 'CO');
    return common.storage;
  }
  if (labelKey === 'confidence_gaps') return common.data;

  return iconForField(labelKey || detail);
}

export function iconForActionKind(kind: string): SemanticIconSpec | undefined {
  return fieldIcons[normalizeKey(kind)] ?? iconForField(kind);
}

export function iconForRuleOutcome(outcome: string): SemanticIconSpec | undefined {
  return ruleOutcomeIcons[normalizeKey(outcome)] ?? iconForField(outcome);
}

export function iconForInfoTerm(term: string, tag: string): SemanticIconSpec | undefined {
  const termIcon = iconForField(term);
  if (termIcon) return termIcon;
  return infoTagIcons[normalizeKey(tag)];
}

export function iconForFieldValue(fieldKey: string | undefined, value: JsonValue): SemanticIconSpec | undefined {
  if (!fieldKey || typeof value !== 'string' || value.trim() === '') return undefined;

  const normalized = normalizeKey(fieldKey);
  const id = value.trim();

  if (normalized === 'outcome' || normalized.endsWith('_outcome')) {
    return iconForRuleOutcome(id);
  }

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
  if (key.includes('hunt')) return item('Gun_Revolver', 'Hunt icon', 'HU');
  if (key.includes('animal')) return item('Meat_Squirrel', 'Animal icon', 'AN');
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

function iconForDomainText(value: string): SemanticIconSpec | undefined {
  const key = normalizeKey(stripLeadingSymbol(value));
  if (key.includes('food') || key.includes('rice') || key.includes('meal') || key.includes('crop') || key.includes('freezer')) return common.food;
  if (key.includes('hunt') || key.includes('hare')) return item('Gun_Revolver', 'Hunt icon', 'HU');
  if (key.includes('power') || key.includes('grid')) return common.power;
  if (key.includes('wood')) return common.storage;
  if (key.includes('research') || key.includes('stonecutting')) return common.research;
  if (key.includes('defense') || key.includes('weapon') || key.includes('perimeter')) return scopeIcons.defense;
  if (key.includes('shelter') || key.includes('structure') || key.includes('building') || key.includes('construction')) return common.construction;
  if (key.includes('wealth') || key.includes('trade')) return common.economy;
  return undefined;
}

function stripLeadingSymbol(value: string): string {
  return value.trimStart().replace(/^[^\p{L}\p{N}]+/u, '').trimStart();
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
