import { findTextIconMatches } from '../../dashboard/textIconMatcher';
import { SemanticIconCue } from './SemanticIcon';

export function IconizedText({
  className = '',
  maxIcons = 3,
  text,
}: {
  className?: string;
  maxIcons?: number;
  text: string;
}) {
  const matches = findTextIconMatches(text, maxIcons);

  if (matches.length === 0) {
    return <>{text}</>;
  }

  const nodes: JSX.Element[] = [];
  let cursor = 0;

  matches.forEach((match, index) => {
    if (match.start > cursor) {
      nodes.push(<span key={`text-${index}`}>{text.slice(cursor, match.start)}</span>);
    }

    nodes.push(
      <span className="iconized-token" key={`icon-${match.start}-${match.end}`}>
        <SemanticIconCue icon={match.icon} size="xs" />
        <span>{text.slice(match.start, match.end)}</span>
      </span>
    );
    cursor = match.end;
  });

  if (cursor < text.length) {
    nodes.push(<span key="text-tail">{text.slice(cursor)}</span>);
  }

  return <span className={`iconized-text ${className}`}>{nodes}</span>;
}
