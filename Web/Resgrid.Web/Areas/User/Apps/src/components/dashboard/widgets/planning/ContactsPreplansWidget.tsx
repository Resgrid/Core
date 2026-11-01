import { EmptyState, ItemList, ProtectedText } from '../../primitives';
import type { DashboardProtectedText } from '../../types';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/ContactsPreplansWidgetData.cs */
export interface ContactsPreplansWidgetData {
  DueCount: number;
  Items: Array<{
    ContactId: string;
    /** 0 person, 1 company. */
    ContactType: number;
    /** Company name (plain) or the person's name (ADP contacts.firstname/lastname). */
    Name?: DashboardProtectedText | null;
    NextReviewDueUtc: string;
    DaysOverdue: number;
    Url: string;
  }>;
}

/** contacts.preplans (plan §7.6, §9A.2): contacts whose pre-plan review date has passed, longest overdue first. */
export default function ContactsPreplansWidget({ data, t, ctx }: WidgetProps<ContactsPreplansWidgetData>) {
  useWidgetBadge(ctx, data.DueCount > 0 ? data.DueCount : null);

  if (data.Items.length === 0) {
    return <EmptyState icon="fa-check" text={t('W.contacts.preplans.Empty')} />;
  }

  return (
    <ItemList
      items={data.Items.map((contact) => {
        const isProtected = !!contact.Name?.IsProtected;
        return {
          key: contact.ContactId,
          icon: contact.ContactType === 1 ? 'fa-building-o' : 'fa-user-o',
          tone: contact.DaysOverdue >= 30 ? 'warn' : undefined,
          // A protected name is a reveal button, which cannot sit inside the row's link.
          title: <ProtectedText value={contact.Name} placeholder={t('W.contacts.preplans.ProtectedContact')} />,
          href: isProtected ? undefined : contact.Url,
          meta: [
            t('W.contacts.preplans.ReviewDue', ctx.format.date(contact.NextReviewDueUtc)),
            contact.DaysOverdue > 0 ? t('W.contacts.preplans.DaysOverdue', contact.DaysOverdue) : t('W.contacts.preplans.DueToday'),
          ].join(' · '),
          right: isProtected ? (
            <a href={contact.Url} aria-label={t('W.contacts.preplans.Open')}>
              <i className="fa fa-angle-right" aria-hidden="true" />
            </a>
          ) : undefined,
        };
      })}
    />
  );
}
