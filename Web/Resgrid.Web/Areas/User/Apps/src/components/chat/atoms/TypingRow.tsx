import type { TypingEntry } from '../chatStore';

interface TypingRowProps {
  entries: TypingEntry[];
  botTyping?: boolean;
}

function describe(entries: TypingEntry[]): string {
  const names = entries.map((entry) => entry.displayName).filter((name) => name.length > 0);
  if (names.length === 0) {
    return 'Someone is typing';
  }
  if (names.length === 1) {
    return `${names[0]} is typing`;
  }
  if (names.length === 2) {
    return `${names[0]} and ${names[1]} are typing`;
  }
  return `${names[0]} and ${names.length - 1} others are typing`;
}

export default function TypingRow({ entries, botTyping }: TypingRowProps) {
  const active = botTyping || entries.length > 0;
  return (
    <div className="rgchat-typing" aria-live="polite">
      {active && (
        <>
          <span className="rgchat-dots">
            <span />
            <span />
            <span />
          </span>{' '}
          {botTyping ? 'Assistant is typing' : describe(entries)}
        </>
      )}
    </div>
  );
}
