import { EmptyState, ItemList, KpiGrid, SubHeading } from '../../primitives';
import type { KpiItem } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { rowsFor, toneIf } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/CostRecoveryQueueWidgetData.cs */
export interface CostRecoveryQueueWidgetData {
  ManagerScope: boolean;
  Total: number;
  Mine: number;
  WithErrors: number;
  Rows: CostRecoveryQueueRow[];
  Readiness?: CostRecoveryReadiness | null;
}

export interface CostRecoveryQueueRow {
  WorkItemId: string;
  RecordType: string;
  LocalState: string;
  DeploymentName?: string | null;
  IncidentNumber?: string | null;
  RequestNumber?: string | null;
  ErrorCount: number;
  WarningCount: number;
  AgeDays: number;
  IsMine: boolean;
}

export interface CostRecoveryReadiness {
  IsReady: boolean;
  Blockers: number;
  Warnings: number;
  AuthorityProfileCurrent: boolean;
  OpenWorkItems: number;
  ReturnedWorkItems: number;
  InvoicesAwaitingLocalApproval: number;
}

const QUEUE_URL = '/User/CalOesMars/Queue';
const READINESS_URL = '/User/CalOesMars';

/** costRecovery.queue (plan §7.9): Cal OES MARS items to work, oldest first; managers also see agency readiness. */
export default function CostRecoveryQueueWidget({ data, size, t, ctx }: WidgetProps<CostRecoveryQueueWidgetData>) {
  const k = 'W.costRecovery.queue.';
  useWidgetBadge(ctx, data.Total || null);

  const readiness = data.Readiness;
  if (!data.Total && !readiness) {
    return <EmptyState icon="fa-balance-scale" text={t(`${k}Empty`)} linkText={t(`${k}OpenQueue`)} href={QUEUE_URL} />;
  }

  const kpis: KpiItem[] = [{ value: ctx.format.number(data.Total, 0), label: t(`${k}Total`), href: QUEUE_URL }];
  if (data.ManagerScope) kpis.push({ value: ctx.format.number(data.Mine, 0), label: t(`${k}Mine`) });
  kpis.push({ value: ctx.format.number(data.WithErrors, 0), label: t(`${k}WithErrors`), tone: toneIf(data.WithErrors, 'bad') });

  return (
    <>
      <KpiGrid columns={kpis.length} items={kpis} />
      {data.Total ? (
        <ItemList
          items={(size === 'm' ? (data.Rows ?? []).slice(0, readiness ? 1 : 3) : rowsFor(data.Rows, size, 2)).map((row) => ({
            key: row.WorkItemId,
            title: [t(`${k}RecordType.${row.RecordType}`), row.DeploymentName].filter(Boolean).join(' · '),
            meta: [t(`${k}State.${row.LocalState}`), row.IncidentNumber, row.RequestNumber].filter(Boolean).join(' · '),
            tone: row.ErrorCount ? 'bad' : row.WarningCount ? 'warn' : undefined,
            right: <span className="rg-mu rg-num" title={t(`${k}Age`)}>{ctx.format.duration(row.AgeDays * 1440)}</span>,
            href: QUEUE_URL,
          }))}
        />
      ) : null}
      {readiness ? (
        <>
          {size !== 'm' ? <SubHeading>{t(`${k}Readiness`)}</SubHeading> : null}
          <ItemList
            items={[
              {
                key: 'ready',
                title: readiness.IsReady ? t(`${k}Ready`) : t(`${k}NotReady`),
                meta: [
                  readiness.Blockers ? `${t(`${k}Blockers`)} ${readiness.Blockers}` : null,
                  readiness.Warnings ? `${t(`${k}Warnings`)} ${readiness.Warnings}` : null,
                  readiness.ReturnedWorkItems ? `${t(`${k}Returned`)} ${readiness.ReturnedWorkItems}` : null,
                  readiness.InvoicesAwaitingLocalApproval ? `${t(`${k}AwaitingLocalApproval`)} ${readiness.InvoicesAwaitingLocalApproval}` : null,
                ]
                  .filter(Boolean)
                  .join(' · ') || undefined,
                tone: readiness.IsReady ? 'good' : 'bad',
                href: READINESS_URL,
              },
            ]}
          />
        </>
      ) : null}
    </>
  );
}
