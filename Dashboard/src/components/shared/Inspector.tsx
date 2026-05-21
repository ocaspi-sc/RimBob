import { useMemo, type ReactNode } from 'react';
import { iconForField, iconForFieldValue } from '../../dashboard/semanticIcons';
import { DisclosureSection } from './DisclosureSection';
import { EmptyState } from './EmptyState';
import {
  isJsonRecord,
  isJsonScalar,
  JsonTree,
  summarizeValue,
  toJsonValue,
  type JsonValue,
} from './JsonTree';
import { MetricCard } from './MetricCard';
import { SemanticLabel } from './SemanticIcon';

type JsonRecord = { [key: string]: JsonValue };
type JsonScalar = null | string | number | boolean;

export interface InspectorTableConfig {
  key: string;
  title?: string;
  preferredColumns?: string[];
}

export interface InspectorSurfaceConfig {
  defaultOpenKeys?: string[];
  hiddenKeys?: string[];
  preferredTables?: InspectorTableConfig[];
  summaryKeys?: string[];
}

export function InspectorSurface({
  config = {},
  value,
}: {
  config?: InspectorSurfaceConfig;
  value: unknown;
}) {
  const json = useMemo(() => toJsonValue(value), [value]);

  if (!isJsonRecord(json)) {
    return <UnknownValue value={json} />;
  }

  const hiddenKeys = new Set(config.hiddenKeys ?? []);
  const summary = (config.summaryKeys ?? [])
    .map(key => ({ key, value: readPath(json, key) }))
    .filter((item): item is { key: string; value: JsonValue } => item.value !== undefined);
  const summaryKeys = new Set(summary.map(item => item.key.split('.')[0]));
  const tableConfigs = new Map((config.preferredTables ?? []).map(table => [table.key, table]));
  const entries = Object.entries(json).filter(([key]) => !hiddenKeys.has(key));
  const scalarEntries = entries.filter(([key, item]) => !summaryKeys.has(key) && isJsonScalar(item));
  const sectionEntries = entries.filter(([key, item]) => !summaryKeys.has(key) && !isJsonScalar(item));

  return (
    <div className="inspector-surface">
      {summary.length > 0 && (
        <div className="metric-grid inspector-summary">
          {summary.map(item => (
            <MetricCard
              key={item.key}
              label={<SemanticLabel icon={iconForField(item.key)}><code>{item.key}</code></SemanticLabel>}
              value={<UnknownValue value={item.value} fieldKey={item.key} compact />}
              tone={toneForKeyValue(item.key, item.value)}
            />
          ))}
        </div>
      )}

      {scalarEntries.length > 0 && (
        <FieldGrid entries={scalarEntries} />
      )}

      {sectionEntries.map(([key, item]) => (
        <InspectorSection
          key={key}
          itemKey={key}
          tableConfig={tableConfigs.get(key)}
          value={item}
          defaultOpen={(config.defaultOpenKeys ?? []).includes(key)}
        />
      ))}

      <DisclosureSection
        title={<SemanticLabel icon={iconForField('raw_payload')}><span>Raw payload</span></SemanticLabel>}
        meta={summarizeValue(json)}
      >
        <JsonTree value={json} />
      </DisclosureSection>
    </div>
  );
}

export function FieldGrid({
  entries,
}: {
  entries: Array<[string, JsonValue]>;
}) {
  if (entries.length === 0) {
    return null;
  }

  return (
    <div className="inspector-field-grid">
      {entries.map(([key, value]) => (
        <div className="inspector-field" key={key}>
          <SemanticLabel className="inspector-field-name" icon={iconForField(key)}><code>{key}</code></SemanticLabel>
          <UnknownValue value={value} fieldKey={key} />
        </div>
      ))}
    </div>
  );
}

export function DynamicTable({
  emptyMessage = 'No rows.',
  hiddenColumns = [],
  maxColumns = 8,
  preferredColumns = [],
  rows,
}: {
  emptyMessage?: string;
  hiddenColumns?: string[];
  maxColumns?: number;
  preferredColumns?: string[];
  rows: unknown[];
}) {
  const records = rows
    .map(row => toJsonValue(row))
    .filter(isJsonRecord);

  if (records.length === 0) {
    return <EmptyState code="NO TABLE ROWS">{emptyMessage}</EmptyState>;
  }

  const columns = inferColumns(records, preferredColumns, hiddenColumns, maxColumns);

  return (
    <div className="dynamic-table-wrap">
      <div className="dynamic-table" style={{ ['--inspector-columns' as string]: columns.length }}>
        <div className="dynamic-row header">
          {columns.map(column => (
            <SemanticLabel key={column} icon={iconForField(column)}><span>{column}</span></SemanticLabel>
          ))}
        </div>
        {records.map((record, rowIndex) => (
          <div className="dynamic-row" key={stableRowKey(record, rowIndex)}>
            {columns.map(column => (
              <span key={column}>
                <UnknownValue value={record[column] ?? null} fieldKey={column} compact />
              </span>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

export function UnknownValue({
  compact = false,
  fieldKey,
  value,
}: {
  compact?: boolean;
  fieldKey?: string;
  value: unknown;
}) {
  const json = toJsonValue(value);

  if (isJsonScalar(json)) {
    const icon = iconForFieldValue(fieldKey, json);
    return icon
      ? <SemanticLabel className="semantic-value" icon={icon}><InspectorScalar value={json} /></SemanticLabel>
      : <InspectorScalar value={json} />;
  }

  if (Array.isArray(json) && json.every(isJsonRecord)) {
    return compact
      ? <span className="inspector-muted">{summarizeValue(json)}</span>
      : <DynamicTable rows={json} />;
  }

  if (compact) {
    return <span className="inspector-muted">{summarizeValue(json)}</span>;
  }

  return <JsonTree value={json} />;
}

function InspectorSection({
  defaultOpen,
  itemKey,
  tableConfig,
  value,
}: {
  defaultOpen: boolean;
  itemKey: string;
  tableConfig?: InspectorTableConfig;
  value: JsonValue;
}) {
  const title = tableConfig?.title ?? itemKey;
  const titleIcon = iconForField(itemKey);

  if (Array.isArray(value) && value.every(isJsonRecord)) {
    return (
      <DisclosureSection
        title={<SemanticLabel icon={titleIcon}><span>{title}</span></SemanticLabel>}
        defaultOpen={defaultOpen}
        meta={summarizeValue(value)}
      >
        <DynamicTable rows={value} preferredColumns={tableConfig?.preferredColumns ?? []} />
      </DisclosureSection>
    );
  }

  if (isJsonRecord(value)) {
    const scalarEntries = Object.entries(value).filter(([, item]) => isJsonScalar(item));
    const nestedEntries = Object.entries(value).filter(([, item]) => !isJsonScalar(item));
    return (
      <DisclosureSection
        title={<SemanticLabel icon={titleIcon}><span>{title}</span></SemanticLabel>}
        defaultOpen={defaultOpen}
        meta={summarizeValue(value)}
      >
        <FieldGrid entries={scalarEntries} />
        {nestedEntries.length > 0 && <JsonTree value={Object.fromEntries(nestedEntries)} />}
      </DisclosureSection>
    );
  }

  return (
    <DisclosureSection
      title={<SemanticLabel icon={titleIcon}><span>{title}</span></SemanticLabel>}
      defaultOpen={defaultOpen}
      meta={summarizeValue(value)}
    >
      <UnknownValue value={value} fieldKey={itemKey} />
    </DisclosureSection>
  );
}

function InspectorScalar({ value }: { value: JsonScalar }) {
  if (value === null) return <span className="inspector-null">null</span>;
  if (typeof value === 'boolean') return <span className="inspector-token">{value ? 'true' : 'false'}</span>;
  if (typeof value === 'number') return <span>{value.toLocaleString()}</span>;
  if (looksLikeIsoDate(value)) return <time dateTime={value}>{formatDate(value)}</time>;
  return <span>{value}</span>;
}

function readPath(record: JsonRecord, path: string): JsonValue | undefined {
  let current: JsonValue | undefined = record;
  for (const part of path.split('.')) {
    if (!current || !isJsonRecord(current) || !(part in current)) {
      return undefined;
    }

    current = current[part];
  }

  return current;
}

function inferColumns(records: JsonRecord[], preferredColumns: string[], hiddenColumns: string[], maxColumns: number): string[] {
  const hidden = new Set(hiddenColumns);
  const counts = new Map<string, number>();
  for (const record of records) {
    for (const key of Object.keys(record)) {
      if (!hidden.has(key)) {
        counts.set(key, (counts.get(key) ?? 0) + 1);
      }
    }
  }

  const preferred = preferredColumns.filter(column => !hidden.has(column) && counts.has(column));
  const inferred = [...counts.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([key]) => key)
    .filter(key => !preferred.includes(key));

  return [...preferred, ...inferred].slice(0, maxColumns);
}

function toneForKeyValue(key: string, value: JsonValue): 'neutral' | 'ok' | 'warn' | 'error' {
  const lowerKey = key.toLowerCase();
  const text = String(value ?? '').toLowerCase();

  if (lowerKey.includes('error') || text.includes('failed') || text.includes('error')) return 'error';
  if (text.includes('warn') || text.includes('degraded') || text.includes('not_exposed')) return 'warn';
  if (text.includes('success') || text.includes('complete') || text.includes('parsed') || text.includes('ok')) return 'ok';
  return 'neutral';
}

function stableRowKey(record: JsonRecord, index: number): string {
  const id = record.id ?? record.key ?? record.name ?? record.minister ?? record.title;
  return isJsonScalar(id) && id !== null ? `${id}` : `row-${index}`;
}

function looksLikeIsoDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}T/.test(value);
}

function formatDate(iso: string): ReactNode {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString();
}
