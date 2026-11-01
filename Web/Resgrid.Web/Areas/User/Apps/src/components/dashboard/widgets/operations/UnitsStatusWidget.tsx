import { DenseTable, EmptyState, Note, ProtectedText, type TableColumn } from '../../primitives';
import type { DashboardProtectedText } from '../../types';
import type { WidgetProps } from '../types';
import { Availability, Chip, isWide, minutesSince, useCollapsedGroups, useNow, type StatusChipData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/UnitsStatusWidgetData.cs */
export interface UnitsStatusRow {
  UnitId: number;
  Name?: string | null;
  Type?: string | null;
  StationGroupId?: number | null;
  StationName?: string | null;
  Status: StatusChipData;
  HasStatus: boolean;
  StatusOnUtc?: string | null;
  UnitStateId: number;
  DestinationName?: string | null;
  DestinationType: number;
  DestinationId?: number | null;
  CrewCount: number;
  Note?: DashboardProtectedText | null;
  Url?: string | null;
}

export interface UnitsStatusGroup {
  Key: string;
  Name?: string | null;
  Units: UnitsStatusRow[];
}

export interface UnitsStatusWidgetData {
  /** "station" | "type" | "none". */
  GroupBy: string;
  Groups: UnitsStatusGroup[];
  TotalCount: number;
  ShownCount: number;
  StationFilterIgnored: boolean;
}

/** units.status (plan §7.2): units by station or type with status, time in status, destination, crew and note. */
export default function UnitsStatusWidget({ data, size, t, ctx }: WidgetProps<UnitsStatusWidgetData>) {
  const k = 'W.units.status.';
  const now = useNow(30000);
  const [collapsed, toggle] = useCollapsedGroups(ctx.instanceId);
  const groups = data.Groups ?? [];

  if (!data.TotalCount) {
    return <EmptyState icon="fa-road" text={t(`${k}Empty`)} linkText={t(`${k}OpenUnits`)} href="/User/Units/Index" />;
  }
  if (!data.ShownCount) {
    return <EmptyState icon="fa-check-circle-o" text={t(`${k}AllAvailable`)} onAction={ctx.openSettings} linkText={t(`${k}ChangeSettings`)} />;
  }

  const wide = isWide(size);
  const showDetails = size !== 'm';
  const columns: TableColumn<UnitsStatusRow>[] = [
    {
      key: 'unit',
      header: t(`${k}ColUnit`),
      render: (row) => (
        <>
          {row.Url ? (
            <a className="rg-ustatus-unit" href={row.Url}>
              {row.Name}
            </a>
          ) : (
            <span className="rg-ustatus-unit">{row.Name}</span>
          )}
          {row.Type && data.GroupBy !== 'type' ? <span className="rg-ustatus-type">{row.Type}</span> : null}
        </>
      ),
    },
    { key: 'status', header: t(`${k}ColStatus`), render: (row) => <Chip chip={row.Status} fallback={t(`${k}NoStatus`)} /> },
    {
      key: 'for',
      header: t(`${k}ColFor`),
      muted: true,
      render: (row) =>
        row.HasStatus && row.StatusOnUtc ? (
          <span title={ctx.format.dateTime(row.StatusOnUtc)}>{ctx.format.duration(minutesSince(row.StatusOnUtc, now))}</span>
        ) : (
          '—'
        ),
    },
  ];
  if (showDetails) {
    columns.push({ key: 'dest', header: t(`${k}ColDestination`), muted: true, maxWidth: wide ? 220 : 150, render: (row) => row.DestinationName || '' });
  }
  if (wide) {
    if (data.GroupBy !== 'station') {
      columns.push({ key: 'station', header: t(`${k}ColStation`), muted: true, render: (row) => row.StationName || '' });
    }
    columns.push({
      key: 'crew',
      header: t(`${k}ColCrew`),
      muted: true,
      render: (row) =>
        row.CrewCount > 0 ? (
          <span title={t(`${k}CrewTitle`, row.CrewCount)}>
            <i className="fa fa-user" aria-hidden="true" /> {row.CrewCount}
          </span>
        ) : (
          '—'
        ),
    });
  }
  if (showDetails) {
    columns.push({
      key: 'note',
      header: t(`${k}ColNote`),
      muted: true,
      maxWidth: wide ? 260 : 140,
      render: (row) => <ProtectedText value={row.Note} placeholder={t(`${k}ProtectedNote`)} />,
    });
  }

  const groupLabel = (group: UnitsStatusGroup) =>
    group.Name || (data.GroupBy === 'type' ? t(`${k}NoType`) : data.GroupBy === 'station' ? t(`${k}NoStation`) : t(`${k}AllUnits`));
  const hidden = data.TotalCount - data.ShownCount;

  return (
    <>
      {data.GroupBy === 'none' || groups.length <= 1 ? (
        <DenseTable
          columns={columns}
          rows={groups.flatMap((g) => g.Units)}
          rowKey={(row) => String(row.UnitId)}
          caption={t(`${k}Caption`)}
        />
      ) : (
        <DenseTable
          columns={columns}
          groups={groups.map((group) => ({
            key: group.Key,
            label: groupLabel(group),
            meta: t(
              `${k}GroupMeta`,
              group.Units.length,
              group.Units.filter((u) => u.Status?.Availability === Availability.Available).length,
            ),
            rows: group.Units,
          }))}
          collapsedGroups={collapsed}
          onToggleGroup={toggle}
          rowKey={(row) => String(row.UnitId)}
          caption={t(`${k}Caption`)}
        />
      )}
      {hidden > 0 || data.StationFilterIgnored ? (
        <div className="rg-ops-footer">
          {hidden > 0 ? <span>{t(`${k}HiddenAvailable`, hidden)}</span> : null}
          {data.StationFilterIgnored ? <Note>{t(`${k}StationFilterIgnored`)}</Note> : null}
        </div>
      ) : null}
    </>
  );
}
