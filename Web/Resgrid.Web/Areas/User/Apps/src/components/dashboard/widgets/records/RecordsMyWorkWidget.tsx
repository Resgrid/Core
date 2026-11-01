import { EmptyState, ItemList, SubHeading } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, joinMeta, rowsFor } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsMyWorkWidgetData.cs */
export interface RecordsMyWorkRecord {
  RecordId: string;
  Number?: string | null;
  DefinitionKey?: string | null;
  /** RmsRecordState: 1 draft, 2 ready for review, 3 returned, 4 approved. */
  State: number;
  CreatedOn: string;
  ReviewDueOn?: string | null;
}

export interface RecordsMyWorkAssignment {
  AssignmentId: string;
  RecordId?: string | null;
  /** complete, review, correct, attach, acknowledge. */
  Purpose?: string | null;
  /** RmsWorkAssignmentState: 1 open, 2 acknowledged. */
  State: number;
  /** RmsWorkAssigneeKind: 1 person, 2 unit, 3 group, 4 command role, 5 dispatch role. */
  AssigneeKind: number;
  DueOn?: string | null;
  Overdue: boolean;
  CreatedOn: string;
}

export interface RecordsMyWorkWidgetData {
  OwnedOutstanding: number;
  Assignments: number;
  AssignmentsOverdue: number;
  Records: RecordsMyWorkRecord[];
  Queue: RecordsMyWorkAssignment[];
}

const PURPOSES: Record<string, string> = { complete: 'Complete', review: 'Review', correct: 'Correct', attach: 'Attach', acknowledge: 'Acknowledge' };

/** records.myWork (plan §7.8): unfinished records the viewer owns and work assigned to them. */
export default function RecordsMyWorkWidget({ data, size, t, ctx }: WidgetProps<RecordsMyWorkWidgetData>) {
  const k = 'W.records.myWork.';
  const total = (data.OwnedOutstanding ?? 0) + (data.Assignments ?? 0);
  useWidgetBadge(ctx, total > 0 ? total : null);

  if (!total) {
    return <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenRecords`)} href="/User/Records" />;
  }

  const recordUrl = (id: string | null | undefined) => (id ? `/User/Records/Details?id=${enc(id)}` : undefined);
  const rows = size === 'l' ? 5 : 2;

  return (
    <>
      {data.Assignments > 0 ? (
        <>
          <SubHeading>{t(`${k}AssignedToMe`, data.Assignments)}</SubHeading>
          <ItemList
            items={rowsFor(data.Queue, size).slice(0, rows).map((assignment) => ({
              key: assignment.AssignmentId,
              title: t(`${k}Purpose${(assignment.Purpose && PURPOSES[assignment.Purpose]) || 'Other'}`),
              href: recordUrl(assignment.RecordId),
              meta: joinMeta(
                assignment.AssigneeKind > 1 ? t(`${k}Via${assignment.AssigneeKind}`) : null,
                assignment.State === 2 ? t(`${k}Acknowledged`) : null,
                assignment.DueOn ? (assignment.Overdue ? t(`${k}OverdueSince`, ctx.format.date(assignment.DueOn)) : t(`${k}DueOn`, ctx.format.date(assignment.DueOn))) : null,
              ),
              tone: assignment.Overdue ? 'bad' : undefined,
              icon: assignment.Overdue ? undefined : 'fa-tasks',
            }))}
          />
        </>
      ) : null}
      {data.OwnedOutstanding > 0 ? (
        <>
          <SubHeading>{t(`${k}MyRecords`, data.OwnedOutstanding)}</SubHeading>
          <ItemList
            items={rowsFor(data.Records, size).slice(0, rows).map((record) => ({
              key: record.RecordId,
              title: record.Number || t(`${k}NoNumber`),
              href: recordUrl(record.RecordId),
              meta: joinMeta(t(`${k}State${record.State}`), record.ReviewDueOn ? t(`${k}ReviewDue`, ctx.format.date(record.ReviewDueOn)) : t(`${k}Started`, ctx.format.ago(record.CreatedOn))),
              tone: record.State === 3 ? 'warn' : undefined,
              icon: record.State === 3 ? undefined : 'fa-file-text-o',
            }))}
          />
        </>
      ) : null}
    </>
  );
}
