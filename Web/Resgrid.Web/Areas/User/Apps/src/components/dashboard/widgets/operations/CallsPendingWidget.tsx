import { useState } from 'react';
import { EmptyState, ItemList } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { PriorityLabel, replyOf, type CallPriorityData, type EndpointData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/CallsPendingWidgetData.cs */
export interface CallsPendingWidgetRow {
  CallId: number;
  Number?: string | null;
  Name?: string | null;
  Type?: string | null;
  Address?: string | null;
  Priority?: CallPriorityData | null;
  /** "pending" | "scheduled". */
  Kind: string;
  LoggedOnUtc: string;
  DispatchOnUtc?: string | null;
  CanDispatchNow: boolean;
  Url?: string | null;
}

export interface CallsPendingWidgetData {
  PendingCount: number;
  ScheduledCount: number;
  Calls: CallsPendingWidgetRow[];
  DispatchNow?: EndpointData | null;
  PendingListUrl?: string | null;
  ScheduledListUrl?: string | null;
}

/** calls.pending (plan §7.1): saved-not-dispatched and scheduled calls, with Dispatch now for dispatchers. */
export default function CallsPendingWidget({ data, t, ctx }: WidgetProps<CallsPendingWidgetData>) {
  const k = 'W.calls.pending.';
  const [busy, setBusy] = useState<number | null>(null);
  const waiting = (data.PendingCount ?? 0) + (data.ScheduledCount ?? 0);
  useWidgetBadge(ctx, waiting > 0 ? waiting : null);

  const calls = data.Calls ?? [];
  if (calls.length === 0) {
    return <EmptyState icon="fa-clock-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenPending`)} href={data.PendingListUrl ?? '/User/Dispatch/PendingCalls'} />;
  }

  const dispatchNow = async (call: CallsPendingWidgetRow) => {
    if (!data.DispatchNow || busy !== null) return;
    setBusy(call.CallId);
    try {
      const result = await ctx.submit(data.DispatchNow.Url, { form: { callId: call.CallId, returnTo: call.Kind }, ajax: true });
      const reply = replyOf(result.json);
      if (result.ok && reply.success) {
        ctx.toast(reply.message || t(`${k}Dispatched`, call.Number || call.Name || ''));
        ctx.refresh();
      } else {
        ctx.toast(reply.message || t(`${k}DispatchFailed`));
      }
    } finally {
      setBusy(null);
    }
  };

  return (
    <>
      <ItemList
        items={calls.map((call) => {
          const scheduled = call.Kind === 'scheduled';
          const when = scheduled
            ? t(`${k}DispatchesAt`, ctx.format.dateTime(call.DispatchOnUtc))
            : t(`${k}SavedAgo`, ctx.format.ago(call.LoggedOnUtc));
          return {
            key: `${call.Kind}-${call.CallId}`,
            icon: scheduled ? 'fa-clock-o' : 'fa-pause-circle-o',
            accentColor: call.Priority?.Color || undefined,
            title: (
              <>
                <PriorityLabel priority={call.Priority} /> {call.Name || call.Type || t(`${k}Untitled`)}
                {call.Number ? <span className="rg-ops-num">{call.Number}</span> : null}
              </>
            ),
            href: call.Url ?? undefined,
            meta: [scheduled ? t(`${k}Scheduled`) : t(`${k}Pending`), call.Address, when].filter(Boolean).join(' · '),
            right:
              call.CanDispatchNow && data.DispatchNow ? (
                <button type="button" className="rg-btn-xs rg-btn-primary" disabled={busy !== null} onClick={() => void dispatchNow(call)}>
                  {busy === call.CallId ? <i className="fa fa-spinner fa-spin" aria-hidden="true" /> : null} {t(`${k}DispatchNow`)}
                </button>
              ) : null,
          };
        })}
      />
      <div className="rg-ops-footer">
        <a href={data.PendingListUrl ?? '/User/Dispatch/PendingCalls'}>{t(`${k}PendingCount`, data.PendingCount)}</a>
        <a href={data.ScheduledListUrl ?? '/User/Dispatch/ScheduledCalls'}>{t(`${k}ScheduledCount`, data.ScheduledCount)}</a>
      </div>
    </>
  );
}
