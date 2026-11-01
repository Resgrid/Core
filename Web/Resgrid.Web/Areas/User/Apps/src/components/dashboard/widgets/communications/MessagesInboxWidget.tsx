import { EmptyState, HeroNumber, ItemList, ProtectedText } from '../../primitives';
import type { DashboardProtectedText } from '../../types';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Communications/MessagesInboxWidgetData.cs */
export interface MessagesInboxWidgetData {
  UnreadCount: number;
  Items: Array<{
    MessageId: number;
    SenderName?: string | null;
    SystemGenerated: boolean;
    /** ADP messages.subject. */
    Subject?: DashboardProtectedText | null;
    SentOnUtc: string;
    /** 0 normal, 1 callback, 2 poll. */
    Type: number;
    Url: string;
  }>;
}

const TYPE_ICON: Record<number, string> = { 1: 'fa-phone', 2: 'fa-bar-chart' };

/** messages.inbox (plan §7.5, §9A): the unread count and the newest unread messages (subjects may be protected). */
export default function MessagesInboxWidget({ data, size, t, ctx }: WidgetProps<MessagesInboxWidgetData>) {
  useWidgetBadge(ctx, data.UnreadCount > 0 ? data.UnreadCount : null);
  const header = ctx.bootstrap.header;
  const compose = header?.CanSendMessage && header.MessageUrl ? header.MessageUrl : undefined;

  if (data.UnreadCount === 0 || data.Items.length === 0) {
    return <EmptyState icon="fa-envelope-o" text={t('W.messages.inbox.Empty')} linkText={compose ? t('W.messages.inbox.Compose') : undefined} href={compose} />;
  }

  const items = size === 's' ? data.Items.slice(0, 2) : data.Items;

  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, marginBottom: 6 }}>
        <HeroNumber value={ctx.format.number(data.UnreadCount, 0)} label={t('W.messages.inbox.Unread')} size="md" />
        {compose ? (
          <a className="rg-btn-xs" href={compose}>
            <i className="fa fa-pencil" aria-hidden="true" /> {t('W.messages.inbox.Compose')}
          </a>
        ) : null}
      </div>
      <ItemList
        items={items.map((message) => {
          const protectedSubject = !!message.Subject?.IsProtected;
          const sender = message.SystemGenerated || !message.SenderName ? t('W.messages.inbox.System') : message.SenderName;
          const kind = message.Type === 1 ? t('W.messages.inbox.Callback') : message.Type === 2 ? t('W.messages.inbox.Poll') : null;
          return {
            key: String(message.MessageId),
            icon: TYPE_ICON[message.Type] ?? 'fa-envelope',
            // A protected subject is a reveal button; a button cannot sit inside the row's link.
            title: <ProtectedText value={message.Subject} placeholder={t('W.messages.inbox.ProtectedSubject')} />,
            href: protectedSubject ? undefined : message.Url,
            meta: [sender, kind, ctx.format.ago(message.SentOnUtc)].filter(Boolean).join(' · '),
            right: protectedSubject ? (
              <a href={message.Url} aria-label={t('W.messages.inbox.Open')}>
                <i className="fa fa-angle-right" aria-hidden="true" />
              </a>
            ) : undefined,
          };
        })}
      />
    </>
  );
}
