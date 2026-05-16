import { useEffect, useState } from 'react';

export function GameIcon({
  className = '',
  decorative = false,
  fallback = '?',
  label,
  size = 'sm',
  src,
}: {
  className?: string;
  decorative?: boolean;
  fallback?: string;
  label: string;
  size?: 'xs' | 'sm' | 'md';
  src: string | null;
}) {
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
  }, [src]);

  const showImage = Boolean(src && !failed);

  return (
    <span
      aria-hidden={decorative ? 'true' : undefined}
      aria-label={!decorative && !showImage ? label : undefined}
      className={`game-icon ${size} ${className}`}
      title={label}
    >
      {showImage ? (
        <img
          alt={decorative ? '' : label}
          decoding="async"
          loading="lazy"
          src={src ?? undefined}
          onError={() => setFailed(true)}
        />
      ) : (
        <span aria-hidden="true" className="game-icon-fallback">{fallback}</span>
      )}
    </span>
  );
}
