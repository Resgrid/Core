import { EmptyState, ItemList, KpiGrid, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Administration/AdminWorkflowsWidgetData.cs */
export interface AdminWorkflowsWidgetData {
  Workflows: number;
  ActiveWorkflows: number;
  Pending: number;
  Running: number;
  Retrying: number;
  Completed24h: number;
  Failed24h: number;
  Total24h: number;
  SuccessPercent24h?: number | null;
  ErrorDetailsProtected: boolean;
  Unhealthy: AdminWorkflowHealthRow[];
}

export interface AdminWorkflowHealthRow {
  WorkflowId: string;
  Name?: string | null;
  IsEnabled: boolean;
  Failed24h: number;
  Total24h: number;
  LastFailureOn?: string | null;
}

const WORKFLOWS_URL = '/User/Workflows';
const PENDING_URL = '/User/Workflows/Pending';

/**
 * admin.workflows (plan §7.11, §9A.2): run health over the last day. Run error text is never sent (the Workflows module has
 * no reveal for it), so a failing workflow shows a plain muted placeholder — never ProtectedText, there is nothing to reveal.
 */
export default function AdminWorkflowsWidget({ data, size, t, ctx }: WidgetProps<AdminWorkflowsWidgetData>) {
  const k = 'W.admin.workflows.';
  useWidgetBadge(ctx, data.Failed24h || null);

  if (!data.Workflows) {
    return <EmptyState icon="fa-cogs" text={t(`${k}NoWorkflows`)} linkText={t(`${k}OpenWorkflows`)} href={WORKFLOWS_URL} />;
  }

  const unhealthy = (data.Unhealthy ?? []).slice(0, size === 's' ? 2 : undefined);
  return (
    <>
      <KpiGrid
        columns={3}
        items={[
          { value: ctx.format.number(data.Running + data.Retrying, 0), label: t(`${k}Running`), href: PENDING_URL },
          { value: ctx.format.number(data.Pending, 0), label: t(`${k}Pending`), href: PENDING_URL },
          { value: ctx.format.number(data.Failed24h, 0), label: t(`${k}Failed`), tone: data.Failed24h ? 'bad' : undefined },
        ]}
      />
      {unhealthy.length > 0 ? (
        <ItemList
          items={unhealthy.map((row) => ({
            key: row.WorkflowId,
            title: row.Name || row.WorkflowId,
            meta: [t(`${k}FailedOf`, row.Failed24h, row.Total24h), row.LastFailureOn ? ctx.format.time(row.LastFailureOn) : null, !row.IsEnabled ? t(`${k}Off`) : null]
              .filter(Boolean)
              .join(' · '),
            tone: 'bad',
            right: data.ErrorDetailsProtected ? (
              <span className="rg-mu" title={t(`${k}ErrorDetailsProtectedHint`)}>
                {t(`${k}ErrorDetailsProtected`)}
              </span>
            ) : (
              <span className="rg-mu">{t(`${k}SeeDetails`)}</span>
            ),
            href: `/User/Workflows/Health?workflowId=${encodeURIComponent(row.WorkflowId)}`,
          }))}
        />
      ) : (
        <Note>
          <i className="fa fa-check-circle" aria-hidden="true" style={{ color: 'var(--rg-good-text)' }} /> {t(`${k}Empty`)}
        </Note>
      )}
      {size !== 's' ? (
        <Note>
          {t(`${k}Active`, data.ActiveWorkflows, data.Workflows)}
          {data.SuccessPercent24h !== null && data.SuccessPercent24h !== undefined ? ` · ${t(`${k}Success`, data.SuccessPercent24h)}` : ''}
        </Note>
      ) : null}
    </>
  );
}
