import type { SemanticIconSpec } from './semanticIcons';

export interface TextIconMatch {
  end: number;
  icon: SemanticIconSpec;
  start: number;
  term: string;
}

interface TextIconCue {
  icon: SemanticIconSpec;
  terms: string[];
}

function item(id: string, label: string, fallback = id.slice(0, 2).toUpperCase()): SemanticIconSpec {
  return { fallback, label, ref: { kind: 'item', id } };
}

const textIconCues: TextIconCue[] = [
  cue(item('MealSimple', 'Food icon', 'FO'), [
    'simple meals',
    'raw food',
    'meals',
    'meal',
    'food',
    'nutrition',
    'hunger',
    'kitchen',
  ]),
  cue(item('Plant_Rice', 'Rice and crop icon', 'RI'), [
    'wild harvest',
    'ready to harvest',
    'rice',
    'crops',
    'crop',
    'harvest',
    'growing',
    'grow',
    'plants',
    'plant',
    'sow',
  ]),
  cue(item('WoodLog', 'Storage icon', 'ST'), [
    'stockpile',
    'stockpiles',
    'storage',
    'store',
    'stored',
    'freezer',
    'wood logs',
    'wood',
  ]),
  cue(item('Steel', 'Labor and material icon', 'LA'), [
    'plant cut',
    'plantcut',
    'labor',
    'work type',
    'work',
    'haul',
    'hauling',
    'steel',
    'materials',
  ]),
  cue(item('ComponentIndustrial', 'Component and power icon', 'CP'), [
    'components',
    'component',
    'power',
    'battery',
    'batteries',
    'electricity',
  ]),
  cue(item('Wall', 'Construction icon', 'CO'), [
    'construction',
    'building',
    'buildings',
    'build',
    'walls',
    'wall',
    'room',
  ]),
  cue(item('Gun_Revolver', 'Defense and threat icon', 'TH'), [
    'active threat',
    'defense',
    'threat',
    'raiders',
    'raider',
    'raid',
    'hunting',
    'hunt',
  ]),
  cue(item('MedicineIndustrial', 'Medical icon', 'ME'), [
    'medicine',
    'medical',
    'health',
    'downed',
    'wounds',
    'wound',
    'injury',
  ]),
  cue(item('SimpleResearchBench', 'Research icon', 'RE'), [
    'research bench',
    'research',
    'technology',
    'tech',
  ]),
  cue(item('CommsConsole', 'Mayor and communications icon', 'MY'), [
    'cabinet direction',
    'state of the union',
    'mayor',
    'agenda',
    'cabinet',
    'comms',
  ]),
  cue(item('Silver', 'Wealth and trade icon', 'EC'), [
    'wealth pressure',
    'wealth',
    'silver',
    'trade',
    'trader',
  ]),
  cue(item('Plant_Rice', 'Season icon', 'SE'), [
    'days to winter',
    'winter',
    'summer',
    'spring',
    'fall',
    'season',
  ]),
  cue(item('Bed', 'Welfare icon', 'WF'), [
    'recreation',
    'welfare',
    'mood',
    'sleep',
    'bed',
  ]),
];

interface TextIconTerm {
  icon: SemanticIconSpec;
  lowerTerm: string;
  term: string;
}

const terms: TextIconTerm[] = textIconCues
  .flatMap(entry => entry.terms.map(term => ({
    icon: entry.icon,
    lowerTerm: term.toLowerCase(),
    term,
  })))
  .sort((left, right) => right.lowerTerm.length - left.lowerTerm.length);

export function findTextIconMatches(text: string, maxIcons: number): TextIconMatch[] {
  if (maxIcons <= 0 || text.trim() === '') return [];

  const lowerText = text.toLowerCase();
  const seenIconRefs = new Set<string>();
  const seenTerms = new Set<string>();
  const matches: TextIconMatch[] = [];
  let index = 0;

  while (index < text.length && matches.length < maxIcons) {
    if (!isTokenBoundary(text[index - 1])) {
      index += 1;
      continue;
    }

    const match = terms.find(term =>
      !seenTerms.has(term.lowerTerm) &&
      !seenIconRefs.has(iconKey(term.icon)) &&
      lowerText.startsWith(term.lowerTerm, index) &&
      isTokenBoundary(text[index + term.lowerTerm.length]));

    if (!match) {
      index += 1;
      continue;
    }

    seenTerms.add(match.lowerTerm);
    seenIconRefs.add(iconKey(match.icon));
    matches.push({
      end: index + match.term.length,
      icon: match.icon,
      start: index,
      term: match.term,
    });
    index += match.term.length;
  }

  return matches;
}

function cue(icon: SemanticIconSpec, terms: string[]): TextIconCue {
  return { icon, terms };
}

function isTokenBoundary(char: string | undefined): boolean {
  return char === undefined || !/[A-Za-z0-9_]/.test(char);
}

function iconKey(icon: SemanticIconSpec): string {
  return `${icon.label}:${icon.ref.kind}:${icon.ref.id}`.toLowerCase();
}
