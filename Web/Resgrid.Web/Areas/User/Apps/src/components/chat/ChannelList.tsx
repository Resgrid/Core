import { channelDisplayName, formatRelativeDay, groupChannels, messagePreview } from './chatFormat';
import { chatStore } from './chatStore';
import Avatar from './atoms/Avatar';
import type { ChatChannelDto } from './types';

interface ChannelListProps {
  channels: ChatChannelDto[];
  activeChannelId: string | null;
  filter?: string;
  onSelect: (channelId: string) => void;
}

export default function ChannelList({ channels, activeChannelId, filter, onSelect }: ChannelListProps) {
  const normalizedFilter = (filter ?? '').trim().toLowerCase();
  const filtered =
    normalizedFilter.length === 0
      ? channels
      : channels.filter((channel) => channelDisplayName(channel).toLowerCase().includes(normalizedFilter));

  const groups = groupChannels(filtered);
  const messagesByChannel = chatStore.getState().messagesByChannel;

  if (groups.length === 0) {
    return <div className="rgchat-empty">No conversations yet.</div>;
  }

  return (
    <div className="rgchat-list">
      {groups.map((group) => (
        <div key={group.key}>
          <div className="rgchat-group__label">{group.label}</div>
          {group.channels.map((channel) => {
            const channelMessages = messagesByChannel[channel.ChatChannelId];
            const lastMessage = channelMessages ? channelMessages[channelMessages.length - 1] : undefined;
            const preview = messagePreview(lastMessage) || channel.Topic || '';
            const isActive = channel.ChatChannelId === activeChannelId;
            return (
              <button
                key={channel.ChatChannelId}
                type="button"
                className={`rgchat-chan${isActive ? ' rgchat-chan--active' : ''}`}
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
          })}
        </div>
      ))}
    </div>
  );
}
