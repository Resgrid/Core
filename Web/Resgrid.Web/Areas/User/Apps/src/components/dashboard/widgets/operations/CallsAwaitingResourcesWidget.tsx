import { useState } from 'react';
import { EmptyState, ItemList } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { PriorityLabel, isCompact, replyOf, type CallPriorityData, type EndpointData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/CallsAwaitingResourcesWidgetData.cs */
export interface CallsAwaitingResourcesUnit {
  UnitId: number;
  Name?: string | null;
  Type?: string | null;
  ResponseSeconds?: number | null;
}

export interface CallsAwaitingResourcesRequirement {
  RequirementId: number;
  Label?: string | null;
  RequiredCount: number;
  ResolvedCount: number;
  SentInstead?: string | null;
  ResponseCode?: string | null;
  ResponseCodeColor?: string | null;
  CreatedOnUtc: string;
  AvailableUnits: CallsAwaitingResourcesUnit[];
}

export interface CallsAwaitingResourcesCall {
  CallId: number;
  Number?: string | null;
  Name?: string | null;
  Address?: string | null;
  Priority?: CallPriorityData | null;
  LoggedOnUtc: string;
  WaitingSinceUtc?: string | null;
  Requirements: CallsAwaitingResourcesRequirement[];
  Url?: string | null;
}

export interface CallsAwaitingResourcesWidgetData {
  TotalCount: number;
  Calls: CallsAwaitingResourcesCall[];
  DispatchUnit?: EndpointData | null;
  Dismiss?: EndpointData | null;
  ListUrl?: string | null;
}

/** calls.awaitingResources (plan §7.1): calls whose run card still needs resources, with the units available now. */
export default function CallsAwaitingResourcesWidget({ data, size, t, ctx }: WidgetProps<CallsAwaitingResourcesWidgetData>) {
  const k = 'W.calls.awaitingResources.';
  const [busy, setBusy] = useState<string | null>(null);
  useWidgetBadge(ctx, data.TotalCount > 0 ? data.TotalCount : null);

  const calls = data.Calls ?? [];
  const listUrl = data.ListUrl ?? '/User/Dispatch/AwaitingResources';
  if (calls.length === 0) {
    return <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenList`)} href={listUrl} />;
  }

  const send = async (call: CallsAwaitingResourcesCall, unit: CallsAwaitingResourcesUnit) => {
    if (!data.DispatchUnit || busy !== null) return;
    const key = `${call.CallId}-${unit.UnitId}`;
    setBusy(key);
    try {
      const result = await ctx.submit(data.DispatchUnit.Url, { form: { callId: call.CallId, unitId: unit.UnitId }, ajax: true });
      const reply = replyOf(result.json);
      if (result.ok && reply.success) {
        ctx.toast(t(`${k}Sent`, unit.Name || '', call.Number || ''));
        ctx.refresh();
      } else {
        ctx.toast(reply.message || t(`${k}SendFailed`));
      }
    } finally {
      setBusy(null);
    }
  };

  const showUnits = !isCompact(size);

  return (
    <>
      <ItemList
        items={calls.map((call) => ({
          key: String(call.CallId),
          icon: 'fa-exclamation-triangle',
          tone: 'warn' as const,
          accentColor: call.Priority?.Color || undefined,
          title: (
            <>
              <PriorityLabel priority={call.Priority} /> {call.Name || t(`${k}Untitled`)}
              {call.Number ? <span className="rg-ops-num">{call.Number}</span> : null}
            </>
          ),
          href: call.Url ?? undefined,
          meta: (
            <>
              {(call.Requirements ?? []).map((req) => {
                const missing = Math.max(0, req.RequiredCount - req.ResolvedCount);
                return (
                  <div key={req.RequirementId} className="rg-awaiting-req">
                    <span>{t(`${k}Needs`, missing || req.RequiredCount, req.Label || '')}</span>
                    {req.SentInstead ? <span>· {t(`${k}SentInstead`, req.SentInstead)}</span> : null}
                    {showUnits && data.DispatchUnit
                      ? (req.AvailableUnits ?? []).map((unit) => (
                          <button
                            key={unit.UnitId}
                            type="button"
                            className="rg-btn-xs"
                            disabled={busy !== null}
                            title={unit.ResponseSeconds ? t(`${k}EtaTitle`, ctx.format.duration(Math.round(unit.ResponseSeconds / 60))) : undefined}
                            onClick={() => void send(call, unit)}
                          >
                            {busy === `${call.CallId}-${unit.UnitId}` ? <i className="fa fa-spinner fa-spin" aria-hidden="true" /> : <i className="fa fa-paper-plane-o" aria-hidden="true" />}{' '}
                            {t(`${k}Send`, unit.Name || '')}
                          </button>
                        ))
                      : null}
                  </div>
                );
              })}
            </>
          ),
          right: <span title={ctx.format.dateTime(call.WaitingSinceUtc ?? call.LoggedOnUtc)}>{ctx.format.ago(call.WaitingSinceUtc ?? call.LoggedOnUtc)}</span>,
        }))}
      />
      {data.TotalCount > calls.length ? (
        <div className="rg-ops-footer">
          <a href={listUrl}>{t(`${k}ShowingOf`, calls.length, data.TotalCount)}</a>
        </div>
      ) : null}
    </>
  );
}
