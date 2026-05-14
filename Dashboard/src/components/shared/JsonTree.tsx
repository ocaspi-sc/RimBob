export type JsonValue =
  | null
  | string
  | number
  | boolean
  | JsonValue[]
  | { [key: string]: JsonValue };

export function JsonTree({ value }: { value: unknown }) {
  return <div className="json-tree">{renderValue(toJsonValue(value), 'root')}</div>;
}

export function tryParseJson(raw: string): unknown {
  try {
    return JSON.parse(raw) as unknown;
  } catch {
    return undefined;
  }
}

export function summarizeValue(value: unknown): string {
  const json = toJsonValue(value);
  if (Array.isArray(json)) return `${json.length} items`;
  if (isRecord(json)) return `${Object.keys(json).length} fields`;
  if (json === null) return 'null';
  return typeof json;
}

function renderValue(value: JsonValue, key: string): JSX.Element {
  if (Array.isArray(value)) {
    if (value.length === 0) return <span className="json-empty">empty list</span>;
    return (
      <ol className="json-list">
        {value.map((item, index) => (
          <li key={`${key}-${index}`}>{renderValue(item, `${key}-${index}`)}</li>
        ))}
      </ol>
    );
  }

  if (isRecord(value)) {
    const entries = Object.entries(value);
    if (entries.length === 0) return <span className="json-empty">empty object</span>;
    return (
      <div className="json-record">
        {entries.map(([entryKey, entryValue]) => (
          <div className="json-field" key={`${key}-${entryKey}`}>
            <span className="json-key">{humanize(entryKey)}</span>
            <div className="json-value">{renderValue(entryValue, `${key}-${entryKey}`)}</div>
          </div>
        ))}
      </div>
    );
  }

  if (value === null) return <span className="json-empty">null</span>;
  if (typeof value === 'boolean') return <span className="json-primitive">{value ? 'true' : 'false'}</span>;
  return <span className="json-primitive">{String(value)}</span>;
}

function toJsonValue(value: unknown): JsonValue {
  if (value === null) return null;
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') return value;
  if (Array.isArray(value)) return value.map(toJsonValue);
  if (typeof value === 'object') {
    return Object.entries(value as Record<string, unknown>).reduce<Record<string, JsonValue>>(
      (acc, [key, item]) => {
        acc[key] = toJsonValue(item);
        return acc;
      },
      {},
    );
  }
  return String(value);
}

function isRecord(value: JsonValue): value is { [key: string]: JsonValue } {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function humanize(key: string): string {
  return key
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, char => char.toUpperCase());
}
