import { EmptyState, ItemList } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Communications/ChatUnreadWidgetData.cs */
export interface ChatUnreadWidgetData {
  TotalUnread: number;
  UnreadChannelCount: number;
  PendingAckCount: number;
  Channels: Array<{
    ChatChannelId: string;
    Name?: string | null;
    /** ChatChannelType: 0 DM, 1 ad-hoc, 2 department, 3 group, 4 custom, 5–7 incident, 8 chatbot, 9 commander line. */
    ChannelType: number;
    UnreadCount: number;
    PendingAcks: number;
    LastMessageOnUtc?: string | null;
    NotificationPreference: number;
  }>;
}

export const CHAT_URL = '/User/Chat';

export function channelIcon(type: number): string {
  switch (type) {
    case 0:
      return 'fa-user';
    case 1:
      return 'fa-users';
    case 5:
    case 6:
    case 7:
    case 9:
      return 'fa-fire';
    case 8:
      return 'fa-magic';
    case 4:
      return 'fa-lock';
    default:
      return 'fa-hashtag';
  }
}

/** chat.unread (plan §7.5): channels with unread messages and urgent messages waiting for the member's acknowledgement. */
export default function ChatUnreadWidget({ data, size, t, ctx }: WidgetProps<ChatUnreadWidgetData>) {
  useWidgetBadge(ctx, data.TotalUnread > 0 ? data.TotalUnread : null);

  if (data.Channels.length === 0) {
    return <EmptyState icon="fa-comments-o" text={t('W.chat.unread.Empty')} linkText={t('W.chat.unread.OpenChat')} href={CHAT_URL} />;
  }

  const channels = size === 's' ? data.Channels.slice(0, 4) : data.Channels;

  return (
    <>
      {data.PendingAckCount > 0 ? (
        <div className="rg-tone-warn" style={{ marginBottom: 4, fontSize: 12 }}>
          <i className="fa fa-exclamation-circle" aria-hidden="true" /> {t('W.chat.unread.PendingAckCount', data.PendingAckCount)}
        </div>
      ) : null}
      <ItemList
        items={channels.map((channel) => ({
          key: channel.ChatChannelId,
          icon: channel.PendingAcks > 0 ? 'fa-exclamation-circle' : channelIcon(channel.ChannelType),
          tone: channel.PendingAcks > 0 ? 'warn' : undefined,
          title: channel.Name || (channel.ChannelType === 0 ? t('W.chat.unread.DirectMessage') : t('W.chat.unread.Channel')),
          href: CHAT_URL,
          meta:
            channel.PendingAcks > 0
              ? t('W.chat.unread.PendingAcks')
              : channel.LastMessageOnUtc
                ? ctx.format.ago(channel.LastMessageOnUtc)
                : undefined,
          right: channel.UnreadCount > 0 ? <span className="rg-w-badge">{ctx.format.number(channel.UnreadCount, 0)}</span> : undefined,
        }))}
      />
    </>
  );
}
