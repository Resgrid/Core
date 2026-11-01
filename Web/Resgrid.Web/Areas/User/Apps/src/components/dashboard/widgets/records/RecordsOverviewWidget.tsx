import { KpiGrid, Note, type KpiItem } from '../../primitives';
import type { WidgetProps } from '../types';
import { toneIf } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsOverviewWidgetData.cs */
export interface RecordsOverviewWidgetData {
  Drafts: number;
  AwaitingReview: number;
  Returned: number;
  Overdue: number;
  IncidentIncomplete: number;
  IncidentAwaitingReview: number;
  DisclosuresOpen: number;
  DisclosuresOverdue: number;
  WarningCount: number;
}

const DASHBOARD_URL = '/User/Records/Dashboard';
const QUEUE_URL = '/User/Records';

/** records.overview (plan §7.8): the Records dashboard work queues, scoped to what the viewer may open. */
export default function RecordsOverviewWidget({ data, size, t, ctx }: WidgetProps<RecordsOverviewWidgetData>) {
  const k = 'W.records.overview.';
  const n = (value: number) => ctx.format.number(value, 0);
  const items: KpiItem[] = [
    { value: n(data.Drafts), label: t(`${k}Drafts`), href: `${QUEUE_URL}?state=1` },
    { value: n(data.AwaitingReview), label: t(`${k}AwaitingReview`), href: `${QUEUE_URL}?state=2` },
    { value: n(data.Returned), label: t(`${k}Returned`), tone: toneIf(data.Returned, 'warn'), href: `${QUEUE_URL}?state=3` },
    { value: n(data.Overdue), label: t(`${k}Overdue`), tone: data.Overdue > 0 ? 'bad' : 'good', href: DASHBOARD_URL },
    { value: n(data.IncidentIncomplete), label: t(`${k}IncidentIncomplete`), href: DASHBOARD_URL },
    { value: n(data.DisclosuresOverdue), label: t(`${k}DisclosuresOverdue`), tone: data.DisclosuresOverdue > 0 ? 'bad' : 'good', href: DASHBOARD_URL },
  ];
  if (size === 'l') {
    items.splice(5, 0, { value: n(data.IncidentAwaitingReview), label: t(`${k}IncidentAwaitingReview`), href: DASHBOARD_URL });
    items.push({ value: n(data.DisclosuresOpen), label: t(`${k}DisclosuresOpen`), href: DASHBOARD_URL });
  }

  return (
    <>
      <KpiGrid columns={size === 'l' ? 4 : 3} items={items} />
      {data.WarningCount > 0 ? (
        <Note>
          <i className="fa fa-exclamation-triangle rg-list-icon rg-tone-warn" aria-hidden="true" /> <a href={DASHBOARD_URL}>{t(`${k}Warnings`, data.WarningCount)}</a>
        </Note>
      ) : null}
    </>
  );
}
