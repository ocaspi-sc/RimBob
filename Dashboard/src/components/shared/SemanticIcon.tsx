import { iconUrlFor } from '../../api/icons';
import type { SemanticIconSpec } from '../../dashboard/semanticIcons';
import { GameIcon } from './GameIcon';
import type { ReactNode } from 'react';

export function SemanticIconCue({
  className = '',
  icon,
  size = 'xs',
}: {
  className?: string;
  icon: SemanticIconSpec | undefined;
  size?: 'xs' | 'sm' | 'md';
}) {
  if (!icon) return null;

  return (
    <GameIcon
      className={`semantic-icon ${className}`}
      decorative
      fallback={icon.fallback}
      label={icon.label}
      size={size}
      src={iconUrlFor(icon.ref)}
    />
  );
}

export function SemanticLabel({
  children,
  className = '',
  icon,
  size = 'xs',
}: {
  children: ReactNode;
  className?: string;
  icon: SemanticIconSpec | undefined;
  size?: 'xs' | 'sm' | 'md';
}) {
  return (
    <span className={`semantic-label ${className}`}>
      <SemanticIconCue icon={icon} size={size} />
      <span className="semantic-label-text">{children}</span>
    </span>
  );
}
