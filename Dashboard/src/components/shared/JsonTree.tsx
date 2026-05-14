import { useCallback, useMemo, useRef } from 'react';
import { JsonView } from 'react-json-view-lite';
import 'react-json-view-lite/dist/index.css';

export type JsonValue =
  | null
  | string
  | number
  | boolean
  | JsonValue[]
  | { [key: string]: JsonValue };

export function JsonTree({
  compactTopLevel = true,
  expandDepth = Number.POSITIVE_INFINITY,
  value,
}: {
  compactTopLevel?: boolean;
  expandDepth?: number;
  value: unknown;
}) {
  const json = useStableJsonValue(value);
  const shouldExpandNode = useCallback((level: number) => {
    const visibleLevel = compactTopLevel ? level : level + 1;
    return visibleLevel <= expandDepth;
  }, [compactTopLevel, expandDepth]);

  if (Array.isArray(json) || isJsonRecord(json)) {
    return (
      <div className="json-tree">
        <JsonView
          data={json}
          compactTopLevel={compactTopLevel}
          clickToExpandNode
          shouldExpandNode={shouldExpandNode}
          style={jsonTreeStyles}
        />
      </div>
    );
  }

  return <div className="json-tree">{renderScalar(json)}</div>;
}

function useStableJsonValue(value: unknown): JsonValue {
  const nextJson = useMemo(() => toJsonValue(value), [value]);
  const nextFingerprint = useMemo(() => stringifyJsonValue(nextJson), [nextJson]);
  const previous = useRef<{ fingerprint: string; json: JsonValue }>();

  if (!previous.current || previous.current.fingerprint !== nextFingerprint) {
    previous.current = { fingerprint: nextFingerprint, json: nextJson };
  }

  return previous.current.json;
}

function stringifyJsonValue(value: JsonValue): string {
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
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
  if (isJsonRecord(json)) return `${Object.keys(json).length} fields`;
  if (json === null) return 'null';
  return typeof json;
}

export function isJsonRecord(value: JsonValue): value is { [key: string]: JsonValue } {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function isJsonScalar(value: JsonValue): value is null | string | number | boolean {
  return value === null || typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean';
}

export function toJsonValue(value: unknown): JsonValue {
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

function renderScalar(value: JsonValue): JSX.Element {
  if (value === null) return <span className="json-empty">null</span>;
  if (typeof value === 'boolean') return <span className="json-primitive">{value ? 'true' : 'false'}</span>;
  return <span className="json-primitive">{String(value)}</span>;
}

const jsonTreeStyles = {
  container: 'json-view-container',
  childFieldsContainer: 'json-child-fields-container',
  basicChildStyle: 'json-basic-child',
  collapseIcon: 'json-collapse-icon',
  expandIcon: 'json-expand-icon',
  collapsedContent: 'json-collapsed-content',
  label: 'json-key',
  clickableLabel: 'json-clickable-label',
  nullValue: 'json-empty',
  undefinedValue: 'json-empty',
  numberValue: 'json-number',
  stringValue: 'json-string',
  booleanValue: 'json-boolean',
  otherValue: 'json-primitive',
  punctuation: 'json-punctuation',
  noQuotesForStringValues: false,
  quotesForFieldNames: false,
  stringifyStringValues: true,
};
