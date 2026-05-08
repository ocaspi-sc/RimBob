// TypeScript mirrors of the wire-shape MayorAgenda. Snake_case keys match
// the JSON produced by RimAI.Core.Advice.* records.

export type AgendaItemStatus = 'active' | 'completed' | 'deferred';

export interface AgendaItem {
  id: string;
  text: string;
  status: AgendaItemStatus;
}

export interface MayorPosture {
  economic: string;   // "growth" | "consolidation" | "survival"
  military: string;   // "defensive" | "offensive" | "neutral"
  summary: string;
}

export interface MayorAgenda {
  version: number;
  updated_in_game_tick: string;
  generated_at: string;  // ISO 8601 UTC
  posture: MayorPosture;
  /** Map of category name → one-line interpretation. Keys: agriculture, defense, welfare, construction, treasury, research. */
  state_of_the_union: Record<string, string>;
  update_notes: string;
  short_term: AgendaItem[];
  long_term: AgendaItem[];
  minister_direction: Record<string, string>;
}
