import { memo } from 'react';
import type { ChatReactionDto } from '../types';

interface ReactionChipsProps {
  reactions: ChatReactionDto[];
  currentUserId: string;
  onToggle: (emoji: string, mine: boolean) => void;
}

interface Grouped {
  emoji: string;
  count: number;
  mine: boolean;
}

function group(reactions: ChatReactionDto[], currentUserId: string): Grouped[] {
  const map = new Map<string, Grouped>();
  for (const reaction of reactions) {
    const existing = map.get(reaction.Emoji) ?? { emoji: reaction.Emoji, count: 0, mine: false };
    existing.count += 1;
    if (reaction.UserId === currentUserId) {
      existing.mine = true;
    }
    map.set(reaction.Emoji, existing);
  }
  return Array.from(map.values());
}

function ReactionChips({ reactions, currentUserId, onToggle }: ReactionChipsProps) {
  if (reactions.length === 0) {
    return null;
  }
  const grouped = group(reactions, currentUserId);
  return (
    <div className="rgchat-reactions">
      {grouped.map((item) => (
        <button
          key={item.emoji}
          type="button"
          className={`rgchat-reaction${item.mine ? ' rgchat-reaction--mine' : ''}`}
          onClick={() => onToggle(item.emoji, item.mine)}
          title={item.mine ? 'Remove reaction' : 'React'}
        >
          <span>{item.emoji}</span>
          <span>{item.count}</span>
        </button>
      ))}
    </div>
  );
}

export default memo(ReactionChips);
