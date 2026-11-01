import { EmptyState, ItemList, KpiGrid } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, isCompact, rowsFor } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/DeploymentsActiveWidgetData.cs */
export interface DeploymentsActiveWidgetData {
  CanViewAll: boolean;
  OpenCount: number;
  MineCount: number;
  Rows: DeploymentsActiveRow[];
}

export interface DeploymentsActiveRow {
  DeploymentId: string;
  Name?: string | null;
  IncidentNumber?: string | null;
  Status: number;
  StatusName: string;
  StartOn?: string | null;
  DaysDeployed?: number | null;
  IsMine: boolean;
}

const LIST_URL = '/User/Deployments';

/** deployments.active (plan §7.9): open deployments, mine first (members see only the ones they are rostered on). */
export default function DeploymentsActiveWidget({ data, size, t, ctx }: WidgetProps<DeploymentsActiveWidgetData>) {
  const k = 'W.deployments.active.';
  useWidgetBadge(ctx, data.OpenCount || null);

  const rows = data.Rows ?? [];
  if (rows.length === 0) {
    return <EmptyState icon="fa-truck" text={t(`${k}Empty`)} linkText={t(`${k}OpenDeployments`)} href={LIST_URL} />;
  }

  return (
    <>
      {data.CanViewAll && size !== 'm' && !isCompact(size) ? (
        <KpiGrid
          columns={2}
          items={[
            { value: ctx.format.number(data.OpenCount, 0), label: t(`${k}Open`), href: LIST_URL },
            { value: ctx.format.number(data.MineCount, 0), label: t(`${k}Mine`) },
          ]}
        />
      ) : null}
      <ItemList
        items={(size === 'm' ? rows.slice(0, 3) : rowsFor(rows, size, 2)).map((row) => ({
          key: row.DeploymentId,
          title: row.Name || row.IncidentNumber || row.DeploymentId,
          meta: [
            row.IncidentNumber && row.Name ? row.IncidentNumber : null,
            row.DaysDeployed !== null && row.DaysDeployed !== undefined ? t(`${k}Day`, row.DaysDeployed + 1) : row.StartOn ? t(`${k}Starts`, ctx.format.date(row.StartOn)) : null,
            row.IsMine ? t(`${k}Rostered`) : null,
          ]
            .filter(Boolean)
            .join(' · '),
          icon: row.IsMine ? 'fa-user' : 'fa-truck',
          right: <span className="rg-tag">{t(`${k}Status.${row.StatusName}`)}</span>,
          href: `/User/Deployments/View?id=${enc(row.DeploymentId)}`,
        }))}
      />
    </>
  );
}
