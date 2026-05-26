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

export async function fetchTrace(scope: ScopeKey, signal?: AbortSignal): Promise<MinisterTrace> {
  return await readJson<MinisterTrace>(`/api/ministers/${scope}/trace/latest`, signal);
}

export async function triggerMinister(scope: ScopeKey, signal?: AbortSignal): Promise<ManualTriggerPayload> {
  return await postJson<ManualTriggerPayload>(`/api/ministers/${scope}/trigger`, signal);
}
