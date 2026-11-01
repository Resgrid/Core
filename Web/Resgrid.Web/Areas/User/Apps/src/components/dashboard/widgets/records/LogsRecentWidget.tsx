import { BarList, EmptyState, ItemList, Note, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import { utcTime } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/LogsRecentWidgetData.cs */
export interface LogsRecentTypeCount {
  /** LogTypes: 1 run, 2 training, 4 work, 5 meeting, 6 coroner, 7 callback (0 = not set). */
  LogType: number;
  Count: number;
}

export interface LogsRecentItem {
  LogId: number;
  LogType: number;
  CallId?: number | null;
  StartedOn?: string | null;
  LoggedOn: string;
}

export interface LogsRecentWidgetData {
  SinceUtc: string;
  Total: number;
  Truncated: boolean;
  ByType: LogsRecentTypeCount[];
  Recent: LogsRecentItem[];
}

const LOGS_URL = '/User/Logs';
const KNOWN_TYPES = [1, 2, 4, 5, 6, 7];

/** logs.recent (plan §7.8): legacy logs written in recent days, by type, with the newest. */
export default function LogsRecentWidget({ data, size, t, ctx }: WidgetProps<LogsRecentWidgetData>) {
  const k = 'W.logs.recent.';
  const since = utcTime(data.SinceUtc);
  const days = since === null ? 7 : Math.max(1, Math.round((Date.now() - since) / 86400000));
  const typeLabel = (type: number) => t(`${k}Type${KNOWN_TYPES.includes(type) ? type : 0}`);

  if (!data.Total) {
    return <EmptyState icon="fa-file" text={t(`${k}Empty`, days)} linkText={t(`${k}OpenLogs`)} href={LOGS_URL} />;
  }

  return (
    <>
      <BarList items={(data.ByType ?? []).map((type) => ({ label: typeLabel(type.LogType), value: type.Count, href: LOGS_URL }))} />
      {size === 'l' && data.Recent?.length ? (
        <>
          <SubHeading>{t(`${k}Newest`)}</SubHeading>
          <ItemList
            items={data.Recent.map((log) => ({
              key: String(log.LogId),
              title: typeLabel(log.LogType),
              href: `${LOGS_URL}/View?logId=${encodeURIComponent(String(log.LogId))}`,
              meta: log.StartedOn ? t(`${k}Started`, ctx.format.dateTime(log.StartedOn)) : undefined,
              right: <span className="rg-mu">{ctx.format.ago(log.LoggedOn)}</span>,
              icon: 'fa-file-text-o',
            }))}
          />
        </>
      ) : null}
      <Note>{data.Truncated ? t(`${k}TotalTruncated`, data.Total, days) : t(`${k}Total`, data.Total, days)}</Note>
    </>
  );
}
