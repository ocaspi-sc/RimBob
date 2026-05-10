export type JsonValue =
  | null
  | string
  | number
  | boolean
  | JsonValue[]
  | { [key: string]: JsonValue };

export type JsonRecord = { [key: string]: JsonValue };

interface KeywordDecoration {
  emoji: string;
  label: string;
  terms: string[];
}

const keywordDecorations: KeywordDecoration[] = [
  { emoji: '🌾', label: 'Food', terms: ['agriculture', 'crop', 'farm', 'food', 'hunger', 'meal', 'nutrition', 'starvation'] },
  { emoji: '🛡️', label: 'Defense', terms: ['armor', 'defense', 'defensive', 'hostile', 'raid', 'threat', 'wall', 'weapon'] },
  { emoji: '❤️', label: 'Welfare', terms: ['break', 'medical', 'mood', 'pain', 'recreation', 'sick', 'welfare'] },
  { emoji: '🩺', label: 'Health', terms: ['dead', 'downed', 'health', 'hediff', 'injury', 'medical', 'patient', 'wound'] },
  { emoji: '👤', label: 'People', terms: ['colonist', 'pawn', 'people', 'prisoner', 'skill', 'trait'] },
  { emoji: '⚡', label: 'Power', terms: ['battery', 'electric', 'grid', 'power', 'solar', 'watt'] },
  { emoji: '🔨', label: 'Construction', terms: ['building', 'construction', 'freezer', 'infrastructure', 'repair', 'room'] },
  { emoji: '💰', label: 'Wealth', terms: ['market', 'silver', 'treasury', 'wealth'] },
  { emoji: '🔬', label: 'Research', terms: ['research', 'science', 'tech'] },
  { emoji: '🌦️', label: 'Weather', terms: ['biome', 'cold', 'heat', 'season', 'temperature', 'weather', 'winter'] },
  { emoji: '⚠️', label: 'Risk', terms: ['critical', 'danger', 'emergency', 'low', 'negative', 'risk', 'warning'] },
  { emoji: '✅', label: 'Status', terms: ['active', 'complete', 'completed', 'done', 'stable'] },
];

export function isJsonRecord(value: JsonValue | undefined): value is JsonRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function keywordIconForText(text: string): string | null {
  return getKeywordDecorations(text, 1)[0]?.emoji ?? null;
}

export function summariseJsonValue(value: JsonValue): string {
  if (Array.isArray(value)) return `${value.length} item${value.length === 1 ? '' : 's'}`;
  if (isJsonRecord(value)) {
    const count = Object.keys(value).length;
    return `${count} field${count === 1 ? '' : 's'}`;
  }
  if (value === null) return 'null';
  return String(value);
}

export function ReadableJsonView({ value }: { value: JsonValue }) {
  return (
    <div className="readable-json">
      <JsonNode value={value} depth={0} />
    </div>
  );
}

export function DecoratedPlainText({ text }: { text: string }) {
  const lines = text.split(/\r?\n/);

  return (
    <div className="prompt-plain-text">
      {lines.map((line, index) => (
        <div
          // Prompt files can repeat identical lines; index is stable for this static view.
          key={`${index}-${line}`}
          className={`prompt-line${line.trim().length === 0 ? ' empty' : ''}`}
        >
          {line.trim().length === 0 ? '\u00a0' : <DecoratedText text={line} />}
        </div>
      ))}
    </div>
  );
}

function JsonNode({ depth, value }: { depth: number; value: JsonValue }) {
  if (Array.isArray(value)) return <JsonArray depth={depth} value={value} />;
  if (isJsonRecord(value)) return <JsonObject depth={depth} value={value} />;
  return <JsonPrimitive value={value} />;
}

function JsonObject({ depth, value }: { depth: number; value: JsonRecord }) {
  const entries = Object.entries(value);
  if (entries.length === 0) {
    return <span className="json-empty">empty object</span>;
  }

  return (
    <div className={`json-object json-depth-${Math.min(depth, 3)}`}>
      {entries.map(([key, fieldValue]) => {
        const isComplex = Array.isArray(fieldValue) || isJsonRecord(fieldValue);
        return (
          <div key={key} className={`json-field${isComplex ? ' complex' : ' primitive'}`}>
            <div className="json-key">
              <KeyIcon text={key} />
              <span>{formatJsonKey(key)}</span>
              {isComplex && <small>{summariseJsonValue(fieldValue)}</small>}
            </div>
            <div className="json-value">
              <JsonNode value={fieldValue} depth={depth + 1} />
            </div>
          </div>
        );
      })}
    </div>
  );
}

function JsonArray({ depth, value }: { depth: number; value: JsonValue[] }) {
  if (value.length === 0) {
    return <span className="json-empty">empty list</span>;
  }

  if (value.every(isJsonPrimitive)) {
    return (
      <ul className="json-chip-list">
        {value.map((item, index) => (
          <li key={index}>
            <JsonPrimitive value={item} />
          </li>
        ))}
      </ul>
    );
  }

  return (
    <div className="json-array">
      {value.map((item, index) => (
        <div key={index} className="json-array-item">
          <div className="json-array-heading">
            <KeyIcon text={summariseArrayItem(item, index)} />
            <strong>{summariseArrayItem(item, index)}</strong>
            <small>{summariseJsonValue(item)}</small>
          </div>
          <JsonNode value={item} depth={depth + 1} />
        </div>
      ))}
    </div>
  );
}

function JsonPrimitive({ value }: { value: null | string | number | boolean }) {
  if (value === null) return <span className="json-null">null</span>;
  if (typeof value === 'boolean') {
    return <span className={`json-boolean ${value ? 'true' : 'false'}`}>{String(value)}</span>;
  }
  if (typeof value === 'number') {
    return <span className="json-number">{Number.isFinite(value) ? value.toLocaleString() : String(value)}</span>;
  }
  if (value.length === 0) return <span className="json-empty">empty string</span>;
  return <DecoratedText text={value} />;
}

function DecoratedText({ text }: { text: string }) {
  const decorations = getKeywordDecorations(text, 4);

  return (
    <span className="decorated-text">
      {decorations.length > 0 && (
        <span className="keyword-strip" aria-hidden>
          {decorations.map(decoration => (
            <span key={decoration.label} className="keyword-badge" title={decoration.label}>
              {decoration.emoji}
            </span>
          ))}
        </span>
      )}
      <span>{text}</span>
    </span>
  );
}

function KeyIcon({ text }: { text: string }) {
  const icon = keywordIconForText(text);
  if (!icon) return <span className="json-key-icon neutral" aria-hidden>#</span>;
  return <span className="json-key-icon" aria-hidden>{icon}</span>;
}

function getKeywordDecorations(text: string, limit: number): KeywordDecoration[] {
  const normalised = text.toLowerCase();
  const matches = keywordDecorations.filter(decoration =>
    decoration.terms.some(term => normalised.includes(term)),
  );
  return matches.slice(0, limit);
}

function isJsonPrimitive(value: JsonValue): value is null | string | number | boolean {
  return value === null || ['string', 'number', 'boolean'].includes(typeof value);
}

function formatJsonKey(key: string): string {
  const spaced = key
    .replace(/_/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .trim();
  if (spaced.length === 0) return key;
  return spaced[0].toUpperCase() + spaced.slice(1);
}

function summariseArrayItem(value: JsonValue, index: number): string {
  if (!isJsonRecord(value)) return `Item ${index + 1}`;

  const preferredKeys = ['name', 'label', 'heading', 'title', 'cite_id', 'id', 'source'];
  for (const key of preferredKeys) {
    const field = value[key];
    if (typeof field === 'string' && field.length > 0) return field;
    if (typeof field === 'number') return `${formatJsonKey(key)} ${field}`;
  }

  return `Item ${index + 1}`;
}
