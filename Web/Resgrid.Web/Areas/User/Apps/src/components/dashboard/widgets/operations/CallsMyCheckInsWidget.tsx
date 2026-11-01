import { useState } from 'react';
import { parseUtc } from '../../i18n';
import { EmptyState, ItemList, type Tone } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { useNow, type EndpointData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/CallsMyCheckInsWidgetData.cs */
export interface CallsMyCheckInTimer {
  CallId: number;
  Number?: string | null;
  Name?: string | null;
  CallStartedOnUtc: string;
  HasTimer: boolean;
  DurationMinutes: number;
  WarningThresholdMinutes: number;
  LastCheckInUtc?: string | null;
  DueUtc?: string | null;
  MinutesRemaining: number;
  NeedsCheckIn: boolean;
  /** "Green" | "Warning" | "Critical" | "NoTimer". */
  Status?: string | null;
  Url?: string | null;
}

export interface CallsMyCheckInsWidgetData {
  Timers: CallsMyCheckInTimer[];
  CheckIn?: EndpointData | null;
  PersonnelCheckInType: number;
}

/** calls.myCheckIns (plan §7.1): the viewer's check-in timers, counting down between refreshes. */
export default function CallsMyCheckInsWidget({ data, t, ctx }: WidgetProps<CallsMyCheckInsWidgetData>) {
  const k = 'W.calls.myCheckIns.';
  const now = useNow(30000);
  const [busy, setBusy] = useState<number | null>(null);

  const timers = (data.Timers ?? []).map((timer) => {
    const due = parseUtc(timer.DueUtc);
    const remaining = timer.HasTimer && due ? (due.getTime() - now) / 60000 : null;
    const tone: Tone | undefined = !timer.HasTimer
      ? undefined
      : remaining !== null && remaining <= 0
        ? 'bad'
        : remaining !== null && remaining <= timer.WarningThresholdMinutes
          ? 'warn'
          : 'good';
    return { timer, remaining, tone };
  });
  const urgent = timers.filter((x) => x.tone === 'bad' || x.tone === 'warn').length;
  useWidgetBadge(ctx, urgent > 0 ? urgent : null);

  if (timers.length === 0) {
    return <EmptyState icon="fa-hourglass-o" text={t(`${k}Empty`)} />;
  }

  const checkIn = async (timer: CallsMyCheckInTimer) => {
    if (!data.CheckIn || busy !== null) return;
    setBusy(timer.CallId);
    try {
      await ctx.post(data.CheckIn.Url, { CallId: timer.CallId, CheckInType: data.PersonnelCheckInType });
      ctx.toast(t(`${k}CheckedIn`, timer.Number || timer.Name || ''));
      ctx.refresh();
    } catch {
      ctx.toast(t(`${k}CheckInFailed`));
    } finally {
      setBusy(null);
    }
  };

  return (
    <ItemList
      items={timers.map(({ timer, remaining, tone }) => {
        let meta: string;
        if (!timer.HasTimer || remaining === null) {
          meta = t(`${k}NoTimer`);
        } else if (remaining <= 0) {
          meta = t(`${k}Overdue`, ctx.format.duration(Math.ceil(-remaining)), ctx.format.duration(timer.DurationMinutes));
        } else {
          meta = t(`${k}DueIn`, ctx.format.duration(Math.ceil(remaining)), ctx.format.duration(timer.DurationMinutes));
        }
        return {
          key: String(timer.CallId),
          icon: timer.HasTimer ? 'fa-hourglass-half' : 'fa-hourglass-o',
          tone,
          title: [timer.Number, timer.Name].filter(Boolean).join(' · ') || t(`${k}Untitled`),
          href: timer.Url ?? undefined,
          meta,
          right:
            timer.HasTimer && data.CheckIn ? (
              <button
                type="button"
                className={`rg-btn-xs${tone === 'bad' || tone === 'warn' ? ' rg-btn-primary' : ''}`}
                disabled={busy !== null}
                onClick={() => void checkIn(timer)}
              >
                {busy === timer.CallId ? <i className="fa fa-spinner fa-spin" aria-hidden="true" /> : null} {t(`${k}CheckIn`)}
              </button>
            ) : null,
        };
      })}
    />
  );
}
