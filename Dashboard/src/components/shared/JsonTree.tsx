import { useMemo, useRef, useState } from 'react';
import { iconForField, iconForFieldValue, iconForRecord } from '../../dashboard/semanticIcons';
import { SemanticIconCue, SemanticLabel } from './SemanticIcon';

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

  return (
    <div className="json-tree json-source-tree">
      <JsonNode
        compactTopLevel={compactTopLevel}
        expandDepth={expandDepth}
        level={0}
        path=""
        root
        value={json}
      />
    </div>
  );
}

function JsonNode({
  compactTopLevel,
  expandDepth,
  level,
  name,
  path,
  root = false,
  value,
}: {
  compactTopLevel: boolean;
  expandDepth: number;
  level: number;
  name?: string;
  path: string;
  root?: boolean;
  value: JsonValue;
}) {
  const visibleLevel = compactTopLevel ? level : level + 1;
  const [open, setOpen] = useState(visibleLevel <= expandDepth);

  if (isJsonScalar(value)) {
    return <ScalarRow name={name} path={path} value={value} />;
  }

  if (root && compactTopLevel) {
    return <NodeChildren compactTopLevel={compactTopLevel} expandDepth={expandDepth} level={level} path={path} value={value} />;
  }

  const icon = name ? iconForField(path || name) : undefined;
  const title = name ?? (Array.isArray(value) ? 'items' : 'payload');

  return (
    <div className={`json-source-node ${Array.isArray(value) ? 'array' : 'object'}`}>
      <button
        type="button"
        className="json-source-toggle"
        aria-expanded={open}
        onClick={() => setOpen(current => !current)}
      >
        <span className={`json-source-caret ${open ? 'open' : ''}`} aria-hidden>{open ? 'v' : '>'}</span>
        <SemanticLabel icon={icon}><code>{title}</code></SemanticLabel>
        <span className="json-punctuation">:</span>
        <small>{summarizeValue(value)}</small>
      </button>
      {open && (
        <NodeChildren
          compactTopLevel={compactTopLevel}
          expandDepth={expandDepth}
          level={level + 1}
          path={path}
          value={value}
        />
      )}
    </div>
  );
}

function NodeChildren({
  compactTopLevel,
  expandDepth,
  level,
  path,
  value,
}: {
  compactTopLevel: boolean;
  expandDepth: number;
  level: number;
  path: string;
  value: JsonValue[] | { [key: string]: JsonValue };
}) {
  if (Array.isArray(value)) {
    return (
      <div className="json-source-children">
        {value.map((item, index) => (
          <ArrayItem
            compactTopLevel={compactTopLevel}
            expandDepth={expandDepth}
            index={index}
            item={item}
            key={index}
            level={level}
            path={`${path}[${index}]`}
          />
        ))}
      </div>
    );
  }

  return (
    <div className="json-source-children">
      {Object.entries(value).map(([key, item]) => (
        <JsonNode
          compactTopLevel={compactTopLevel}
          expandDepth={expandDepth}
          key={key}
          level={level}
          name={key}
          path={path ? `${path}.${key}` : key}
          value={item}
        />
      ))}
    </div>
  );
}

function ArrayItem({
  compactTopLevel,
  expandDepth,
  index,
  item,
  level,
  path,
}: {
  compactTopLevel: boolean;
  expandDepth: number;
  index: number;
  item: JsonValue;
  level: number;
  path: string;
}) {
  const visibleLevel = compactTopLevel ? level : level + 1;
  const [open, setOpen] = useState(visibleLevel <= expandDepth);

  if (isJsonScalar(item)) {
    return (
      <div className="json-source-row array-scalar">
        <span className="json-source-marker">-</span>
        {renderScalar(item)}
      </div>
    );
  }

  const icon = isJsonRecord(item) ? iconForRecord(item) : undefined;

  return (
    <div className="json-source-array-item">
      <button
        type="button"
        className="json-source-toggle array-item"
        aria-expanded={open}
        onClick={() => setOpen(current => !current)}
      >
        <span className={`json-source-caret ${open ? 'open' : ''}`} aria-hidden>{open ? 'v' : '>'}</span>
        <span className="json-source-marker">-</span>
        <SemanticIconCue icon={icon} size="xs" />
        <code>item {index + 1}</code>
        <small>{summarizeValue(item)}</small>
      </button>
      {open && (
        <NodeChildren
          compactTopLevel={compactTopLevel}
          expandDepth={expandDepth}
          level={level + 1}
          path={path}
          value={item}
        />
      )}
    </div>
  );
}

function ScalarRow({
  name,
  path,
  value,
}: {
  name?: string;
  path: string;
  value: null | string | number | boolean;
}) {
  const icon = name ? iconForField(path || name) : undefined;
  const valueIcon = iconForFieldValue(name, value);

  return (
    <div className="json-source-row scalar">
      {name && (
        <>
          <SemanticLabel icon={icon}><code>{name}</code></SemanticLabel>
          <span className="json-punctuation">:</span>
        </>
      )}
      {valueIcon ? (
        <SemanticLabel className="semantic-value" icon={valueIcon}>{renderScalar(value)}</SemanticLabel>
      ) : (
        renderScalar(value)
      )}
    </div>
  );
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

function renderScalar(value: null | string | number | boolean): JSX.Element {
  if (value === null) return <span className="json-empty">null</span>;
  if (typeof value === 'boolean') return <span className="json-boolean">{value ? 'true' : 'false'}</span>;
  if (typeof value === 'number') return <span className="json-number">{value.toLocaleString()}</span>;
  return <span className="json-string">{value}</span>;
}
