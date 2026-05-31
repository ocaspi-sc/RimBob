import type { ScopeKey } from '../dashboard/scopes';
import type { GameDate } from '../types/colony';
import type { MinisterTrace } from '../types/system';
import { postJson, readJson } from './http';

export interface PromptPayload {
  system: string;
  user: string;
}

export interface RawLlmOutputPayload {
  minister: string;
  provider: string;
  model: string;
  apiKeyIndex: number | null;
  apiKeyLabel: string | null;
  capturedAt: string;
  latencyMs: number;
  status: string;
  parseMode: string;
  systemPromptChars: number;
  userPromptChars: number;
  text: string;
}

export interface ManualTriggerPayload {
  triggered: boolean;
  scope: string;
  minister?: string;
  trigger: string;
}

export interface FoodCropCandidate {
  cropDef: string;
  label: string;
  harvestedThingDef: string;
  baseGrowDays: number;
  growDays: number;
  terrainFertility: number | null;
  harvestYield: number;
  harvestNutrition: number;
  tiles: number;
  projectedNutrition: number;
  projectedDaysAdded: number;
  daysToWinter: number | null;
  daysToWinterMargin: number | null;
  fitsSeason: boolean;
  classificationConfidence: number;
  storageMultiplier: number;
  score: number;
  reason: string;
}

export interface FoodCropMathPayload {
  briefingVersion: number;
  gameTick: number;
  date: GameDate;
  season: {
    currentSeason: string;
    daysToNextSeason: number | null;
    daysToWinter: number | null;
  };
  colonistCount: number;
  estimatedDaysOfFood: number | null;
  nutritionSource: string;
  growingTerrain: {
    hasTerrain: boolean;
    growableCells: number;
    bestFertility: number | null;
    averageFertility: number | null;
    fertilityBands: Array<{
      def: string;
      label: string | null;
      fertility: number;
      cells: number;
    }>;
  };
  bestCandidate: FoodCropCandidate | null;
  candidates: FoodCropCandidate[];
}

export interface WillieSolverRequestPayload {
  request: string;
  reason: string;
  targetClass: string;
  targetDef: string | null;
  roomClass: string | null;
  requestedFrom: string | null;
  sourceMinister: string | null;
  priority: string | null;
}

export interface WillieSolverMetricValue {
  id: string;
  rawValue: number | null;
  unit: string | null;
  normalized: number;
  weight: number;
  contribution: number;
  better: string;
}

export interface WillieSolverDraftTrace {
  generatorId: string;
  anchorRoomId: string | null;
  status: string;
  reason: string | null;
  metrics: WillieSolverMetricValue[];
  diversityReason: string | null;
}

export interface WillieSolverTrace {
  selectedRule: string;
  drafts: WillieSolverDraftTrace[];
  notes: string[];
}

export interface WillieSolverOutputPayload {
  status: string;
  noFit: string | null;
  draftable: string | null;
  placementValid: string | null;
  materialsReady: string | null;
  applyReady: string | null;
  trace: WillieSolverTrace | null;
  errorType: string | null;
  errorMessage: string | null;
}

export interface WillieSolverPayload extends WillieSolverOutputPayload {
  minister: string;
  request: WillieSolverRequestPayload | null;
  gameTick: number | null;
  capturedAt: string;
  output: WillieSolverOutputPayload;
}

export async function fetchBriefing(scope: ScopeKey, signal?: AbortSignal): Promise<unknown> {
  return await readJson<unknown>(`/api/briefings/${scope}/latest`, signal);
}

export async function fetchFoodCropMath(signal?: AbortSignal): Promise<FoodCropMathPayload> {
  return await readJson<FoodCropMathPayload>('/api/ministers/food/crop-math/latest', signal);
}

export async function fetchPrompt(scope: ScopeKey, signal?: AbortSignal): Promise<PromptPayload> {
  return await readJson<PromptPayload>(`/api/ministers/${scope}/prompt`, signal);
}

export async function fetchRawLlmOutput(scope: ScopeKey, signal?: AbortSignal): Promise<RawLlmOutputPayload> {
  return await readJson<RawLlmOutputPayload>(`/api/ministers/${scope}/llm-output/latest`, signal);
}

export async function fetchSolver(scope: ScopeKey, signal?: AbortSignal): Promise<WillieSolverPayload> {
  return await readJson<WillieSolverPayload>(`/api/ministers/${scope}/solver/latest`, signal);
}

export async function fetchTrace(scope: ScopeKey, signal?: AbortSignal): Promise<MinisterTrace> {
  return await readJson<MinisterTrace>(`/api/ministers/${scope}/trace/latest`, signal);
}

export async function triggerMinister(scope: ScopeKey, signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>(`/api/ministers/${scope}/trigger`, signal);
}
