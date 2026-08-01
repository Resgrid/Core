import { memo } from 'react';
import { channelDisplayName, formatRelativeDay, groupChannels, messagePreview } from './chatFormat';
import { useChatStore } from './useChatStore';
import Avatar from './atoms/Avatar';
import type { ChatChannelDto, ChatMessageDto } from './types';

interface ChannelListProps {
  channels: ChatChannelDto[];
  activeChannelId: string | null;
  filter?: string;
  loading?: boolean;
  onSelect: (channelId: string) => void;
}

interface ChannelRowProps {
  channel: ChatChannelDto;
  active: boolean;
  preview: string;
  onSelect: (channelId: string) => void;
}

const ChannelRow = memo(function ChannelRow({ channel, active, preview, onSelect }: ChannelRowProps) {
  return (
    <button
      type="button"
      className={`rgchat-chan${active ? ' rgchat-chan--active' : ''}`}
      onClick={() => onSelect(channel.ChatChannelId)}
    >
      <Avatar name={channelDisplayName(channel)} size="md" />
      <div className="rgchat-chan__body">
        <div className="rgchat-chan__name">{channelDisplayName(channel)}</div>
        <div className="rgchat-chan__preview">{preview}</div>
      </div>
      <div className="rgchat-chan__meta">
        <span className="rgchat-chan__time">{formatRelativeDay(channel.LastMessageOn)}</span>
        {channel.UnreadCount > 0 && (
          <span className="rgchat-badge">{channel.UnreadCount > 99 ? '99+' : channel.UnreadCount}</span>
        )}
      </div>
    </button>
  );
});

const EMPTY_MESSAGES: ChatMessageDto[] = [];

export function ChannelListSkeleton() {
  return (
    <div className="rgchat-list" aria-hidden="true">
      {[0, 1, 2, 3, 4].map((index) => (
        <div key={index} className="rgchat-skeletonrow rgchat-skeletonrow--chan">
          <span className="rgchat-skeleton rgchat-skeleton--avatar" />
          <span className="rgchat-skeleton rgchat-skeleton--line" />
        </div>
      ))}
    </div>
  );
}

export default function ChannelList({ channels, activeChannelId, filter, loading, onSelect }: ChannelListProps) {
  const messagesByChannel = useChatStore((state) => state.messagesByChannel);

  if (loading) {
    return <ChannelListSkeleton />;
  }

  const normalizedFilter = (filter ?? '').trim().toLowerCase();
  const filtered =
    normalizedFilter.length === 0
      ? channels
      : channels.filter((channel) => channelDisplayName(channel).toLowerCase().includes(normalizedFilter));

  const groups = groupChannels(filtered);

  if (groups.length === 0) {
    return <div className="rgchat-empty">No conversations yet.</div>;
  }

  return (
    <div className="rgchat-list">
      {groups.map((group) => (
        <div key={group.key}>
          <div className="rgchat-group__label">{group.label}</div>
          {group.channels.map((channel) => {
            const channelMessages = messagesByChannel[channel.ChatChannelId] ?? EMPTY_MESSAGES;
            const lastMessage = channelMessages[channelMessages.length - 1];
            const preview = messagePreview(lastMessage) || channel.Topic || '';
            return (
              <ChannelRow
                key={channel.ChatChannelId}
                channel={channel}
                active={channel.ChatChannelId === activeChannelId}
                preview={preview}
                onSelect={onSelect}
              />
            );
          })}
        </div>
      ))}
    </div>
  );
}
