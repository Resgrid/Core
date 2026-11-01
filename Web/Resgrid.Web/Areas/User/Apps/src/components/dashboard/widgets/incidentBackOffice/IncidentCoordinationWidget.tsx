import { EmptyState, ItemList, KpiGrid } from '../../primitives';
import type { WidgetProps } from '../types';
import { ReadOnlyNote } from './iboShared';
import './ibo.css';

/** Core/Resgrid.Model/Dashboard/Widgets/IncidentBackOffice/IncidentCoordinationWidgetData.cs */
export interface IncidentCoordinationWidgetData {
  ReadOnly: boolean;
  Boards: number;
  ActiveShares: number;
  StaleLanes: number;
  Rows: IncidentCoordinationBoardSummaryRow[];
}

export interface IncidentCoordinationBoardSummaryRow {
  BoardId: string;
  Name?: string | null;
  ActivationId?: string | null;
  IsFrozen: boolean;
  Lanes: number;
  StaleLanes: number;
  ActiveShares: number;
  LastUpdateOn: string;
}

const BOARDS_URL = '/User/CoordinationBoards';

/** ibo.coordination (plan §7.10): your coordination boards with stale lanes and active partner shares. */
export default function IncidentCoordinationWidget({ data, size, t, ctx }: WidgetProps<IncidentCoordinationWidgetData>) {
  const k = 'W.ibo.coordination.';
  const rows = data.Rows ?? [];
  if (!data.Boards) {
    return (
      <>
        <EmptyState icon="fa-th" text={t(`${k}Empty`)} linkText={t(`${k}OpenBoards`)} href={BOARDS_URL} />
        <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
      </>
    );
  }

  return (
    <>
      <KpiGrid
        columns={3}
        items={[
          { value: ctx.format.number(data.Boards, 0), label: t(`${k}Boards`), href: BOARDS_URL },
          { value: ctx.format.number(data.ActiveShares, 0), label: t(`${k}ActiveShares`) },
          { value: ctx.format.number(data.StaleLanes, 0), label: t(`${k}StaleLanes`), tone: data.StaleLanes ? 'warn' : undefined },
        ]}
      />
      <ItemList
        items={rows.slice(0, size === 'm' ? 2 : undefined).map((row) => ({
          key: row.BoardId,
          title: row.Name || row.BoardId,
          meta: [t(`${k}Lanes`, row.Lanes), row.ActiveShares ? t(`${k}Shares`, row.ActiveShares) : null, t(`${k}Updated`, ctx.format.ago(row.LastUpdateOn))]
            .filter(Boolean)
            .join(' · '),
          tone: row.StaleLanes ? 'warn' : undefined,
          icon: row.StaleLanes ? undefined : 'fa-th',
          right: row.IsFrozen ? (
            <span className="rg-tag">{t(`${k}Frozen`)}</span>
          ) : row.StaleLanes ? (
            <span className="rg-mu">{t(`${k}StaleCount`, row.StaleLanes)}</span>
          ) : undefined,
          href: BOARDS_URL,
        }))}
      />
      <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
    </>
  );
}
