import { EmptyState, ItemList, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, joinMeta, rowsFor } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsInvestigationsWidgetData.cs */
export interface RecordsInvestigationItem {
  CaseId: string;
  CaseNumber?: string | null;
  /** RmsInvestigationCaseState: 1 open, 2 active, 3 pending review, 4 closed. */
  State: number;
  OpenedOn: string;
}

export interface RecordsInvestigationsWidgetData {
  Open: number;
  Cases: RecordsInvestigationItem[];
}

const CASES_URL = '/User/RecordInvestigations';

/** records.investigations (plan §7.8): the viewer's open cases — case number and state only. */
export default function RecordsInvestigationsWidget({ data, size, t, ctx }: WidgetProps<RecordsInvestigationsWidgetData>) {
  const k = 'W.records.investigations.';
  useWidgetBadge(ctx, data.Open > 0 ? data.Open : null);

  if (!data.Open) {
    return <EmptyState icon="fa-search" text={t(`${k}Empty`)} linkText={t(`${k}OpenCases`)} href={CASES_URL} />;
  }

  const cases = rowsFor(data.Cases, size);
  return (
    <>
      <ItemList
        items={cases.map((item) => ({
          key: item.CaseId,
          title: <span className="rg-num">{item.CaseNumber || t(`${k}NoNumber`)}</span>,
          href: `/User/RecordInvestigations/Details?id=${enc(item.CaseId)}`,
          meta: joinMeta(t(`${k}State${item.State}`), t(`${k}Opened`, ctx.format.date(item.OpenedOn))),
          icon: item.State === 3 ? 'fa-hourglass-half' : 'fa-search',
        }))}
      />
      {data.Open > cases.length ? (
        <Note>
          <a href={CASES_URL}>{t(`${k}SeeAll`, data.Open)}</a>
        </Note>
      ) : null}
    </>
  );
}
