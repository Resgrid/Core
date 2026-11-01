import { EmptyState, ItemList, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, joinMeta } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsDeploymentsWidgetData.cs */
export interface RecordsDeploymentItem {
  OrderId: string;
  RecordId?: string | null;
  OrderNumber?: string | null;
  IncidentNumber?: string | null;
  /** RmsExternalOrderStatus: 1 open, 2 mobilized, 3 released, 4 closed out. */
  Status: number;
  MobilizedOn?: string | null;
  CreatedOn: string;
}

export interface RecordsDeploymentsWidgetData {
  Open: number;
  Deployments: RecordsDeploymentItem[];
}

const DEPLOYMENTS_URL = '/User/RecordDeployments';

/** records.deployments (plan §7.8): open deployment records from external resource orders. */
export default function RecordsDeploymentsWidget({ data, t, ctx }: WidgetProps<RecordsDeploymentsWidgetData>) {
  const k = 'W.records.deployments.';
  useWidgetBadge(ctx, data.Open > 0 ? data.Open : null);

  if (!data.Open) {
    return <EmptyState icon="fa-truck" text={t(`${k}Empty`)} linkText={t(`${k}OpenDeployments`)} href={DEPLOYMENTS_URL} />;
  }

  const deployments = data.Deployments ?? [];
  return (
    <>
      <ItemList
        items={deployments.map((order) => ({
          key: order.OrderId,
          title: <span className="rg-num">{order.OrderNumber || order.IncidentNumber || t(`${k}NoNumber`)}</span>,
          href: `${DEPLOYMENTS_URL}/Details?id=${enc(order.OrderId)}`,
          meta: joinMeta(
            t(`${k}Status${order.Status}`),
            order.OrderNumber && order.IncidentNumber ? t(`${k}Incident`, order.IncidentNumber) : null,
            order.MobilizedOn ? t(`${k}Mobilized`, ctx.format.date(order.MobilizedOn)) : t(`${k}Created`, ctx.format.date(order.CreatedOn)),
          ),
          icon: order.Status === 2 ? 'fa-truck' : 'fa-file-text-o',
        }))}
      />
      {data.Open > deployments.length ? (
        <Note>
          <a href={DEPLOYMENTS_URL}>{t(`${k}SeeAll`, data.Open)}</a>
        </Note>
      ) : null}
    </>
  );
}
