import { EmptyState, ItemList, KpiGrid, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, isCompact, isPast, joinMeta, rowsFor, toneIf, toTone } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/ChecklistsDueWidgetData.cs */
export interface ChecklistsDueItem {
  OccurrenceId: string;
  Name?: string | null;
  TargetName?: string | null;
  TargetType: number;
  State: number;
  PeriodStartUtc?: string | null;
  WindowEndUtc?: string | null;
  Overdue: boolean;
  CanStart: boolean;
  Assigned: boolean;
  Tone?: string | null;
}

export interface ChecklistsDueWidgetData {
  DueNow: number;
  Overdue: number;
  Upcoming: number;
  Total: number;
  HasMore: boolean;
  NextDueUtc?: string | null;
  AssignedOnly: boolean;
  Items: ChecklistsDueItem[];
}

const DUE_URL = '/User/Checklists/Due';

/** checklists.due (plan §7.7): overdue, due-now and upcoming checklist occurrences with Start. */
export default function ChecklistsDueWidget({ data, size, t, ctx }: WidgetProps<ChecklistsDueWidgetData>) {
  const k = 'W.checklists.due.';
  const attention = (data.Overdue ?? 0) + (data.DueNow ?? 0);
  useWidgetBadge(ctx, attention > 0 ? attention : null);

  if (!data.Total) {
    return <EmptyState icon="fa-check-square-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenDue`)} href={DUE_URL} />;
  }

  const items = rowsFor(data.Items, size);
  const when = (item: ChecklistsDueItem) => {
    if (item.Overdue) {
      return t(`${k}OverdueSince`, ctx.format.ago(item.WindowEndUtc ?? item.PeriodStartUtc));
    }
    return isPast(item.PeriodStartUtc) ? t(`${k}DueBy`, ctx.format.time(item.WindowEndUtc)) : t(`${k}Opens`, ctx.format.dateTime(item.PeriodStartUtc));
  };

  return (
    <>
      {!isCompact(size) ? (
        <KpiGrid
          columns={3}
          items={[
            { value: data.Overdue, label: t(`${k}Overdue`), tone: toneIf(data.Overdue, 'bad'), href: DUE_URL },
            { value: data.DueNow, label: t(`${k}DueNow`), tone: toneIf(data.DueNow, 'warn'), href: DUE_URL },
            { value: data.Upcoming, label: t(`${k}Upcoming`), href: DUE_URL },
          ]}
        />
      ) : null}
      <ItemList
        items={items.map((item) => {
          const tone = toTone(item.Tone);
          const href = `/User/Checklists/Occurrence?id=${enc(item.OccurrenceId)}`;
          return {
            key: item.OccurrenceId,
            title: item.Name || t(`${k}Unnamed`),
            href,
            meta: joinMeta(item.TargetName, when(item)),
            tone,
            icon: tone === 'info' || !tone ? 'fa-check-square-o' : undefined,
            right: item.CanStart ? (
              <a className="rg-btn-xs rg-btn-primary" href={href}>
                {t(`${k}Start`)}
              </a>
            ) : null,
          };
        })}
      />
      {data.HasMore || data.Items.length > items.length ? (
        <Note>
          <a href={DUE_URL}>{t(`${k}SeeAll`)}</a>
        </Note>
      ) : data.NextDueUtc && !isCompact(size) ? (
        <Note>{t(`${k}NextDue`, ctx.format.dateTime(data.NextDueUtc))}</Note>
      ) : null}
    </>
  );
}
