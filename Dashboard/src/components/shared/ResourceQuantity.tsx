import { itemIconUrl } from '../../api/icons';
import { GameIcon } from './GameIcon';

export interface ResourceQuantityItem {
  approx?: boolean;
  defName: string;
  label?: string | null;
  quantity?: number | null;
}

export function ResourceQuantity({
  approx = false,
  className = '',
  defName,
  label,
  quantity,
}: ResourceQuantityItem & {
  className?: string;
}) {
  const displayLabel = label?.trim() || formatResourceLabel(defName);
  const fallback = initials(defName || displayLabel);

  return (
    <span className={`resource-quantity ${className}`.trim()}>
      <GameIcon
        className="resource-quantity-icon"
        fallback={fallback}
        label={`${displayLabel} icon`}
        size="xs"
        src={itemIconUrl(defName)}
      />
      {quantity !== null && quantity !== undefined && (
        <strong className="resource-quantity-count">{approx ? '~' : ''}{formatResourceNumber(quantity)}</strong>
      )}
      <span className="resource-quantity-label">{displayLabel}</span>
    </span>
  );
}

export function ResourceQuantityList({
  className = '',
  empty = null,
  items,
}: {
  className?: string;
  empty?: string | null;
  items: ResourceQuantityItem[];
}) {
  const visible = items.filter(item => item.defName.trim() !== '');

  if (visible.length === 0) return empty;

  return (
    <span className={`resource-quantity-list ${className}`.trim()}>
      {visible.map((item, index) => (
        <ResourceQuantity
          approx={item.approx}
          defName={item.defName}
          key={`${item.defName}:${item.quantity ?? 'none'}:${index}`}
          label={item.label}
          quantity={item.quantity}
        />
      ))}
    </span>
  );
}

export function formatResourceLabel(value: string): string {
  return value
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2');
}

function formatResourceNumber(value: number): string {
  return Math.round(value).toLocaleString();
}

function initials(value: string): string {
  const trimmed = value.trim();
  if (!trimmed) return '?';
  return trimmed.slice(0, 2).toUpperCase();
}
