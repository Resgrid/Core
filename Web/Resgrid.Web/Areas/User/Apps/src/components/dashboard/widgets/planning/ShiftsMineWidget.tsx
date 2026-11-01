import { EmptyState, ItemList, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import { dayLabel, timeRange } from './planningTime';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/ShiftsMineWidgetData.cs */
export interface ShiftsMineWidgetData {
  Signups: Array<{
    ShiftSignupId: number;
    ShiftId: number;
    ShiftDayId?: number | null;
    ShiftName?: string | null;
    Color?: string | null;
    GroupName?: string | null;
    StartUtc: string;
    EndUtc: string;
    ApprovalPending: boolean;
    HasTrade: boolean;
    Url: string;
  }>;
  TradeRequests: Array<{
    ShiftSignupTradeId: number;
    ShiftName?: string | null;
    Color?: string | null;
    StartUtc: string;
    EndUtc: string;
    RequesterName?: string | null;
    Url: string;
  }>;
  YourShiftsUrl: string;
}

/** shifts.mine (plan §7.6): the member's upcoming shifts and the open trade requests sent to them. */
export default function ShiftsMineWidget({ data, size, t, ctx }: WidgetProps<ShiftsMineWidgetData>) {
  if (data.Signups.length === 0 && data.TradeRequests.length === 0) {
    return <EmptyState icon="fa-briefcase" text={t('W.shifts.mine.Empty')} linkText={t('W.shifts.mine.OpenYourShifts')} href={data.YourShiftsUrl} />;
  }

  const day = (utc: string) => dayLabel(utc, ctx.format, t, 'W.shifts.mine.Today', 'W.shifts.mine.Tomorrow');
  const signups = size === 's' ? data.Signups.slice(0, data.TradeRequests.length > 0 ? 2 : 3) : data.Signups;
  const trades = size === 's' ? data.TradeRequests.slice(0, 1) : data.TradeRequests;

  return (
    <>
      {signups.length > 0 ? (
        <ItemList
          items={signups.map((signup) => ({
            key: `s${signup.ShiftSignupId}`,
            title: `${day(signup.StartUtc)} · ${signup.ShiftName || t('W.shifts.mine.Shift')}`,
            href: signup.Url,
            accentColor: signup.Color || undefined,
            meta: [timeRange(ctx.format, signup.StartUtc, signup.EndUtc), signup.GroupName].filter(Boolean).join(' · '),
            right: signup.ApprovalPending ? (
              <span className="rg-tag">{t('W.shifts.mine.AwaitingApproval')}</span>
            ) : signup.HasTrade ? (
              <span className="rg-tag">{t('W.shifts.mine.TradeRequested')}</span>
            ) : undefined,
          }))}
        />
      ) : null}
      {trades.length > 0 ? (
        <>
          <SubHeading>{t('W.shifts.mine.TradeRequests')}</SubHeading>
          <ItemList
            items={trades.map((trade) => ({
              key: `t${trade.ShiftSignupTradeId}`,
              icon: 'fa-exchange',
              title: `${day(trade.StartUtc)} · ${trade.ShiftName || t('W.shifts.mine.Shift')}`,
              href: trade.Url,
              meta: trade.RequesterName ? t('W.shifts.mine.From', trade.RequesterName) : timeRange(ctx.format, trade.StartUtc, trade.EndUtc),
              right: (
                <a className="rg-btn-xs" href={trade.Url}>
                  {t('W.shifts.mine.Respond')}
                </a>
              ),
            }))}
          />
        </>
      ) : null}
    </>
  );
}
