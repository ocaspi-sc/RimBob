// Mirror of RimAI.Core.Briefings.MayorBriefing — only the fields the sidebar uses.
// ASP.NET Core's default JsonSerializer emits camelCase, so keys are camelCase here.

export interface DateStamp {
  raw: string;
  year: number | null;
  quadrum: string | null;
  day: number | null;
  hour: number | null;
}

export interface SeasonContext {
  currentSeason: string | null;
  daysToNextSeason: number | null;
  daysToWinter: number | null;
}

export interface ColonistsSummary {
  count: number;
  adults: number;
  children: number;
  pawns?: PawnLine[];
  additionalNotShown?: number | null;
}

export interface PawnLine {
  name: string;
  age: number;
  mood: number;
  health: number;
  hunger: number;
  isDowned: boolean;
  currentJob: string | null;
  topSkill: string | null;
}

export interface MedicalState {
  downed: number;
  sick: number;
  surgeryPending: number;
}

export interface FoodSnapshot {
  totalCrops: number;
  readyToHarvest: number;
  estimatedFoodUnitsInStockpile: number;
  estimatedDaysOfFood: number | null;
}

export interface PowerSnapshot {
  productionW: number;
  consumptionW: number;
  storedWd: number;
  capacityWd: number;
  netW: number;
}

export interface MoodSnapshot {
  averageMood: number;
  breakRiskCount: number;
  stressedCount: number;
  contentCount: number;
}

export interface ThreatSnapshot {
  activeRaid: boolean;
  hostileLordCount: number;
  totalThreatPoints: number;
}

export interface WealthSnapshot {
  colony: number;
  colonistCount: number;
  wealthPerColonist: number;
}

export interface WeatherSnapshot {
  def: string | null;
  temperatureC: number;
  rainRate: number;
}

export interface ResearchSnapshot {
  currentProject: string | null;
  progress: number | null;
}

export interface ColonySnapshot {
  briefingVersion: number;
  date: DateStamp;
  gameTick: number;
  season: SeasonContext;
  colonists: ColonistsSummary;
  medical: MedicalState;
  prisoners: number;
  food: FoodSnapshot;
  power: PowerSnapshot;
  mood: MoodSnapshot;
  threat: ThreatSnapshot;
  wealth: WealthSnapshot;
  weather: WeatherSnapshot;
  research: ResearchSnapshot;
}
