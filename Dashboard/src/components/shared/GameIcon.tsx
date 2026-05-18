import { useEffect, useState } from 'react';

export function GameIcon({
  className = '',
  decorative = false,
  fallback = '?',
  fallbackSrc = null,
  label,
  size = 'sm',
  src,
}: {
  className?: string;
  decorative?: boolean;
  fallback?: string;
  fallbackSrc?: string | null;
  label: string;
  size?: 'xs' | 'sm' | 'md';
  src: string | null;
}) {
  const [failedFallback, setFailedFallback] = useState(false);
  const [failedPrimary, setFailedPrimary] = useState(false);

  useEffect(() => {
    setFailedFallback(false);
    setFailedPrimary(false);
  }, [fallbackSrc, src]);

  const usableFallbackSrc = fallbackSrc && fallbackSrc !== src ? fallbackSrc : null;
  const activeSrc = src && !failedPrimary
    ? src
    : usableFallbackSrc && !failedFallback
      ? usableFallbackSrc
      : null;
  const showImage = Boolean(activeSrc);

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
          src={activeSrc ?? undefined}
          onError={() => {
            if (activeSrc === src) {
              setFailedPrimary(true);
            } else {
              setFailedFallback(true);
            }
          }}
        />
      ) : (
        <span aria-hidden="true" className="game-icon-fallback">{fallback}</span>
      )}
    </span>
  );
}
