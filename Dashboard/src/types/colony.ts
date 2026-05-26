// Mirror of RimBob.Core.Briefings.MayorBriefing — only the fields the sidebar uses.
// ASP.NET Core's default JsonSerializer emits camelCase, so keys are camelCase here.

export interface GameDate {
  rawRimWorldDate: string;
  rimWorldYear: number | null;
  quadrum: string | null;
  quadrumDay: number | null;
  hour: number | null;
  gameTick: number;
  totalDays: number;
  completedDays: number;
  colonyDay: number;
  colonyYear: number;
  dayOfYear: number;
  label: string;
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
  id: string;
  name: string;
  age: number;
  mood: number;
  health: number;
  hunger: number;
  isDowned: boolean;
  currentJob: string | null;
  topSkill: string | null;
}

export interface CropBreakdown {
  def: string;
  count: number;
  averageGrowth: number;
}

export interface MedicalState {
  downed: number;
  sick: number;
  surgeryPending: number;
}

export interface FoodSnapshot {
  totalCrops: number;
  readyToHarvest: number;
  cropBreakdown?: CropBreakdown[];
  estimatedFoodUnitsInStockpile: number;
  estimatedDaysOfFood: number | null;
}

export interface ResourceSnapshot {
  materials: Record<string, number>;
  medicine: Record<string, number>;
  weapons: Record<string, number>;
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
  date: GameDate;
  gameTick: number;
  season: SeasonContext;
  colonists: ColonistsSummary;
  medical: MedicalState;
  prisoners: number;
  food: FoodSnapshot;
  resources?: ResourceSnapshot;
  power: PowerSnapshot;
  mood: MoodSnapshot;
  threat: ThreatSnapshot;
  wealth: WealthSnapshot;
  weather: WeatherSnapshot;
  research: ResearchSnapshot;
}
