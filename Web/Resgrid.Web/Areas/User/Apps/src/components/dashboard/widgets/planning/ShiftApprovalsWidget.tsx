import { EmptyState, ItemList, KpiGrid } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { dayLabel } from './planningTime';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/ShiftApprovalsWidgetData.cs */
export interface ShiftApprovalsWidgetData {
  HasApprovals: boolean;
  PendingSignupCount: number;
  PendingTradeCount: number;
  Items: Array<{
    Kind: 'signup' | 'trade' | string;
    Id: number;
    ShiftName?: string | null;
    Color?: string | null;
    GroupName?: string | null;
    UserName?: string | null;
    RequesterName?: string | null;
    StartUtc: string;
    EndUtc: string;
    Url: string;
  }>;
  ApprovalsUrl: string;
}

/** shifts.approvals (plan §7.6): signups and trades waiting for the viewer's decision. */
export default function ShiftApprovalsWidget({ data, size, t, ctx }: WidgetProps<ShiftApprovalsWidgetData>) {
  const total = data.PendingSignupCount + data.PendingTradeCount;
  useWidgetBadge(ctx, data.HasApprovals && total > 0 ? total : null);

  if (!data.HasApprovals) {
    return <EmptyState icon="fa-check-circle-o" text={t('W.shifts.approvals.NotApprover')} />;
  }
  if (total === 0) {
    return <EmptyState icon="fa-check" text={t('W.shifts.approvals.Empty')} />;
  }

  return (
    <>
      <KpiGrid
        items={[
          { value: ctx.format.number(data.PendingSignupCount, 0), label: t('W.shifts.approvals.Signups'), href: data.ApprovalsUrl },
          { value: ctx.format.number(data.PendingTradeCount, 0), label: t('W.shifts.approvals.Trades'), href: data.ApprovalsUrl, tone: data.PendingTradeCount > 0 ? 'warn' : undefined },
        ]}
      />
      {size === 'm' && data.Items.length > 0 ? (
        <div style={{ marginTop: 8 }}>
          <ItemList
            items={data.Items.map((item) => ({
              key: `${item.Kind}${item.Id}`,
              icon: item.Kind === 'trade' ? 'fa-exchange' : 'fa-user-plus',
              accentColor: item.Color || undefined,
              title: `${dayLabel(item.StartUtc, ctx.format, t, 'W.shifts.approvals.Today', 'W.shifts.approvals.Tomorrow')} · ${item.ShiftName || ''}`,
              href: item.Url,
              meta:
                item.Kind === 'trade'
                  ? t('W.shifts.approvals.TradeFromTo', item.RequesterName || '—', item.UserName || '—')
                  : [item.UserName, item.GroupName].filter(Boolean).join(' · '),
              right: <span className="rg-tag">{item.Kind === 'trade' ? t('W.shifts.approvals.Trade') : t('W.shifts.approvals.Signup')}</span>,
            }))}
          />
        </div>
      ) : null}
    </>
  );
}
