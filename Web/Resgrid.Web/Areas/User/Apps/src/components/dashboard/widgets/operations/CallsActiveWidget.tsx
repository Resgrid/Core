import { EmptyState, ItemList, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { PriorityLabel, isCompact, useNewKeys, useNow, type CallPriorityData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/CallsActiveWidgetData.cs */
export interface CallsActiveWidgetRow {
  CallId: number;
  Number?: string | null;
  Name?: string | null;
  Type?: string | null;
  Address?: string | null;
  Priority?: CallPriorityData | null;
  LoggedOnUtc: string;
  UnitCount: number;
  PersonnelCount: number;
  HasIncidentCommand: boolean;
  Url?: string | null;
}

export interface CallsActiveWidgetData {
  TotalCount: number;
  Calls: CallsActiveWidgetRow[];
  NewCallUrl?: string | null;
  GroupFilterIgnored: boolean;
}

/** calls.active (plan §7.1): dispatched active calls in the viewer's dispatch area, newest first. */
export default function CallsActiveWidget({ data, size, t, ctx, definition }: WidgetProps<CallsActiveWidgetData>) {
  const k = 'W.calls.active.';
  useNow(60000);
  useWidgetBadge(ctx, data.TotalCount > 0 ? data.TotalCount : null);
  const calls = data.Calls ?? [];
  const fresh = useNewKeys(calls.map((c) => String(c.CallId)));

  if (calls.length === 0) {
    return (
      <EmptyState
        icon="fa-check-circle-o"
        text={t(`${k}Empty`)}
        linkText={data.NewCallUrl ? t(`${k}NewCall`) : undefined}
        href={data.NewCallUrl ?? undefined}
      />
    );
  }

  const compact = isCompact(size) || size === 'm';

  return (
    <>
      <ItemList
        items={calls.map((call) => {
          const counts: string[] = [];
          if (call.UnitCount > 0) counts.push(t(`${k}Units`, call.UnitCount));
          if (call.PersonnelCount > 0) counts.push(t(`${k}Personnel`, call.PersonnelCount));
          return {
            key: String(call.CallId),
            accentColor: call.Priority?.Color || undefined,
            isNew: fresh.has(String(call.CallId)),
            title: (
              <>
                <PriorityLabel priority={call.Priority} /> {call.Name || call.Type || t(`${k}Untitled`)}
                {call.Number ? <span className="rg-ops-num">{call.Number}</span> : null}
              </>
            ),
            href: call.Url ?? undefined,
            meta: (
              <>
                {!compact && call.Type && call.Name ? <span>{call.Type} · </span> : null}
                {call.Address ? (
                  <>
                    <i className="fa fa-map-marker" aria-hidden="true" /> {call.Address}{' '}
                  </>
                ) : null}
                {counts.map((count) => (
                  <span key={count} className="rg-tag">
                    {count}
                  </span>
                ))}
                {call.HasIncidentCommand ? (
                  <span className="rg-tag" title={t(`${k}IncidentCommandTitle`)}>
                    {t(`${k}IncidentCommand`)}
                  </span>
                ) : null}
              </>
            ),
            right: <span title={ctx.format.dateTime(call.LoggedOnUtc)}>{ctx.format.ago(call.LoggedOnUtc)}</span>,
          };
        })}
      />
      {data.TotalCount > calls.length || data.GroupFilterIgnored ? (
        <div className="rg-ops-footer">
          {data.TotalCount > calls.length ? (
            <a href={definition.ModuleUrl ?? '/User/Dispatch/Dashboard'}>{t(`${k}ShowingOf`, calls.length, data.TotalCount)}</a>
          ) : null}
          {data.GroupFilterIgnored ? <Note>{t(`${k}GroupFilterIgnored`)}</Note> : null}
        </div>
      ) : null}
    </>
  );
}
