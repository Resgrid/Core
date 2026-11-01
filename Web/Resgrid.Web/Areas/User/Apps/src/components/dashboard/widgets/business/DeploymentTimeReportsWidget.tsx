import { EmptyState, KpiGrid, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { isCompact, toneIf } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/DeploymentTimeReportsWidgetData.cs */
export interface DeploymentTimeReportsWidgetData {
  AwaitingApproval: number;
  ApprovedUnbilled: number;
  OldestAwaitingOn?: string | null;
  OldestUnbilledApprovedOn?: string | null;
}

const LIST_URL = '/User/Deployments';

/** deployments.timeReports (plan §7.9): daily time reports for approvers — waiting for approval, approved but not billed. */
export default function DeploymentTimeReportsWidget({ data, size, t, ctx }: WidgetProps<DeploymentTimeReportsWidgetData>) {
  const k = 'W.deployments.timeReports.';
  useWidgetBadge(ctx, data.AwaitingApproval || null);

  if (!data.AwaitingApproval && !data.ApprovedUnbilled) {
    return <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenDeployments`)} href={LIST_URL} />;
  }

  return (
    <>
      <KpiGrid
        columns={2}
        items={[
          { value: ctx.format.number(data.AwaitingApproval, 0), label: t(`${k}AwaitingApproval`), tone: toneIf(data.AwaitingApproval, 'warn'), href: LIST_URL },
          { value: ctx.format.number(data.ApprovedUnbilled, 0), label: t(`${k}ApprovedUnbilled`), href: LIST_URL },
        ]}
      />
      {!isCompact(size) && data.OldestAwaitingOn ? <Note>{t(`${k}OldestSince`, ctx.format.date(data.OldestAwaitingOn))}</Note> : null}
    </>
  );
}
