import { EmptyState, ItemList, KpiGrid } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { channelIcon } from './ChatUnreadWidget';

/** Core/Resgrid.Model/Dashboard/Widgets/Communications/ChatModerationWidgetData.cs */
export interface ChatModerationWidgetData {
  OpenCount: number;
  CountCapped: boolean;
  OldestFlaggedOnUtc?: string | null;
  Items: Array<{
    ChatMessageFlagId: string;
    ChatChannelId?: string | null;
    ChannelName?: string | null;
    /** ChatChannelType, -1 when the channel is gone. */
    ChannelType: number;
    Reason: number;
    ReasonKey: string;
    FlaggedOnUtc: string;
  }>;
}

const MODERATION_URL = '/User/Moderation';

/** chat.moderation (plan §7.5): open chat flags for department administrators, oldest first. */
export default function ChatModerationWidget({ data, size, t, ctx }: WidgetProps<ChatModerationWidgetData>) {
  const count = data.CountCapped ? t('W.chat.moderation.CountCapped', data.OpenCount) : ctx.format.number(data.OpenCount, 0);
  useWidgetBadge(ctx, data.OpenCount > 0 ? count : null);

  if (data.OpenCount === 0) {
    return <EmptyState icon="fa-check" text={t('W.chat.moderation.Empty')} />;
  }

  return (
    <>
      <KpiGrid
        items={[
          { value: count, label: t('W.chat.moderation.OpenFlags'), tone: 'warn', href: MODERATION_URL },
          { value: ctx.format.ago(data.OldestFlaggedOnUtc), label: t('W.chat.moderation.OldestLabel') },
        ]}
      />
      {size === 'm' ? (
        <div style={{ marginTop: 8 }}>
          <ItemList
            items={data.Items.map((flag) => ({
              key: flag.ChatMessageFlagId,
              icon: flag.ChannelType >= 0 ? channelIcon(flag.ChannelType) : 'fa-flag',
              title: t(`W.chat.moderation.Reason.${flag.ReasonKey}`),
              meta: [flag.ChannelName || (flag.ChannelType === 0 ? t('W.chat.unread.DirectMessage') : t('W.chat.moderation.UnknownChannel')), ctx.format.ago(flag.FlaggedOnUtc)].join(' · '),
              href: MODERATION_URL,
            }))}
          />
        </div>
      ) : null}
    </>
  );
}
