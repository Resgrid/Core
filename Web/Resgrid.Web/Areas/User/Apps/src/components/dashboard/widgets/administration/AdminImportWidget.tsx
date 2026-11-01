import { BarList, EmptyState, KpiGrid, Note } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Administration/AdminImportWidgetData.cs */
export interface AdminImportWidgetData {
  OpenWorkspaces: number;
  ActivatedWorkspaces: number;
  FinishedWorkspaces: number;
  Current?: AdminImportWorkspace | null;
  LastActivityOn?: string | null;
}

export interface AdminImportWorkspace {
  WorkspaceId: string;
  Name?: string | null;
  /** ImportWorkspaceStatus name. */
  Status: string;
  LastActivityOn: string;
  TargetActivationDate?: string | null;
  Datasets: number;
  NeedingReview: number;
  Ready: number;
  Imported: number;
  Failed: number;
  RowsTotal: number;
  RowsImported: number;
  RowsWithWarnings: number;
  RowsBlocked: number;
  RunsActive: number;
  RunsFailed: number;
}

const IMPORT_URL = '/User/DepartmentImport';

/** admin.import (plan §7.11): Department Import — the workspace being worked, data sets needing review and running imports. */
export default function AdminImportWidget({ data, size, t, ctx }: WidgetProps<AdminImportWidgetData>) {
  const k = 'W.admin.import.';
  const current = data.Current;
  if (!current) {
    return (
      <>
        <EmptyState icon="fa-upload" text={t(`${k}Empty`)} linkText={t(`${k}OpenImport`)} href={IMPORT_URL} />
        {data.ActivatedWorkspaces ? <Note>{t(`${k}ActivatedCount`, data.ActivatedWorkspaces)}</Note> : null}
      </>
    );
  }

  const href = `/User/DepartmentImport/Workspace?id=${encodeURIComponent(current.WorkspaceId)}`;
  const percent = current.RowsTotal > 0 ? Math.round((current.RowsImported * 100) / current.RowsTotal) : 0;
  return (
    <>
      <div className="rg-m" style={{ marginBottom: 6 }}>
        <a href={href}>
          <b>{current.Name || t(`${k}Untitled`)}</b>
        </a>{' '}
        <span className="rg-tag">{t(`${k}Status.${current.Status}`)}</span>
      </div>
      <BarList
        max={100}
        items={[{ label: t(`${k}Rows`, ctx.format.number(current.RowsImported, 0), ctx.format.number(current.RowsTotal, 0)), value: percent, display: ctx.format.percent(percent), href }]}
      />
      <KpiGrid
        columns={size === 's' ? 2 : 4}
        items={[
          { value: ctx.format.number(current.NeedingReview, 0), label: t(`${k}NeedingReview`), tone: current.NeedingReview ? 'warn' : undefined, href },
          { value: ctx.format.number(current.Failed + current.RunsFailed, 0), label: t(`${k}Failed`), tone: current.Failed + current.RunsFailed ? 'bad' : undefined, href },
          ...(size === 's'
            ? []
            : [
                { value: ctx.format.number(current.Ready, 0), label: t(`${k}Ready`) },
                { value: ctx.format.number(current.RunsActive, 0), label: t(`${k}RunsActive`) },
              ]),
        ]}
      />
      {size !== 's' ? (
        <Note>
          {[
            current.RowsBlocked ? `${t(`${k}Blocked`)} ${ctx.format.number(current.RowsBlocked, 0)}` : null,
            current.RowsWithWarnings ? `${t(`${k}Warnings`)} ${ctx.format.number(current.RowsWithWarnings, 0)}` : null,
            current.TargetActivationDate ? t(`${k}Target`, ctx.format.date(current.TargetActivationDate)) : null,
            t(`${k}LastActivity`, ctx.format.ago(current.LastActivityOn)),
          ]
            .filter(Boolean)
            .join(' · ')}
        </Note>
      ) : null}
    </>
  );
}
