import { EmptyState, ItemList, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, joinMeta, rowsFor, toTone } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/WorkOrdersMineWidgetData.cs */
export interface WorkOrdersMineItem {
  Id: string;
  Number?: string | null;
  Title?: string | null;
  /** WorkOrderStatus 0–9. */
  Status: number;
  /** WorkOrderPriority: 0 low, 1 normal, 2 high, 3 emergency. */
  Priority: number;
  DueOn?: string | null;
  UpdatedOn: string;
  Overdue: boolean;
  Tone?: string | null;
}

export interface WorkOrdersMineWidgetData {
  Open: number;
  Overdue: number;
  HasMore: boolean;
  Items: WorkOrdersMineItem[];
}

const MINE_URL = '/User/WorkOrders?AssignedToMe=true';

/** workOrders.mine (plan §7.7): open work orders assigned to the viewer, most urgent first. */
export default function WorkOrdersMineWidget({ data, size, t, ctx }: WidgetProps<WorkOrdersMineWidgetData>) {
  const k = 'W.workOrders.mine.';
  useWidgetBadge(ctx, data.Open > 0 ? data.Open : null);

  if (!data.Open) {
    return <EmptyState icon="fa-wrench" text={t(`${k}Empty`)} linkText={t(`${k}OpenWorkOrders`)} href={MINE_URL} />;
  }

  const items = rowsFor(data.Items, size, 4);
  return (
    <>
      <ItemList
        items={items.map((order) => ({
          key: order.Id,
          title: order.Title || order.Number || t(`${k}Untitled`),
          href: `/User/WorkOrders/Detail?id=${enc(order.Id)}`,
          meta: joinMeta(order.Title ? order.Number : null, t(`${k}Priority${order.Priority}`), t(`${k}Status${order.Status}`)),
          tone: toTone(order.Tone) === 'info' ? undefined : toTone(order.Tone),
          icon: toTone(order.Tone) === 'bad' || toTone(order.Tone) === 'warn' ? undefined : 'fa-wrench',
          right: order.DueOn ? (
            <span className={order.Overdue ? undefined : 'rg-mu'}>
              {order.Overdue ? t(`${k}OverdueOn`, ctx.format.date(order.DueOn)) : t(`${k}DueOn`, ctx.format.date(order.DueOn))}
            </span>
          ) : null,
        }))}
      />
      {data.HasMore || data.Items.length > items.length ? (
        <Note>
          <a href={MINE_URL}>{t(`${k}SeeAll`)}</a>
        </Note>
      ) : null}
    </>
  );
}
