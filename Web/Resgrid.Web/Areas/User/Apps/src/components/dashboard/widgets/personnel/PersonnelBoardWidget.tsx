import { useMemo, useState } from 'react';
import { DenseTable, EmptyState, Note, ProtectedText, type TableColumn } from '../../primitives';
import type { DashboardProtectedText } from '../../types';
import type { WidgetProps } from '../types';
import { Availability, Chip, isWide, minutesSince, useCollapsedGroups, useNewKeys, useNow, type EndpointData, type StatusChipData } from '../operations/opsShared';
import './personnelWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Personnel/PersonnelBoardWidgetData.cs */
export interface PersonnelBoardRow {
  UserId: string;
  Name?: string | null;
  RoleNames?: string | null;
  Status: StatusChipData;
  StatusOnUtc?: string | null;
  StatusDestination?: string | null;
  /** 0 none, 1 station, 2 call, 3 POI. */
  StatusDestinationType: number;
  StatusDestinationId?: number | null;
  Staffing: StatusChipData;
  StaffingOnUtc?: string | null;
  StaffingNote?: DashboardProtectedText | null;
  CanEdit: boolean;
  Url?: string | null;
}

export interface PersonnelBoardGroup {
  GroupId?: number | null;
  ParentGroupId?: number | null;
  Name?: string | null;
  IsViewerGroup: boolean;
  CanReset: boolean;
  People: PersonnelBoardRow[];
}

export interface PersonnelBoardWidgetData {
  Groups: PersonnelBoardGroup[];
  TotalCount: number;
  CustomStatuses: boolean;
  CanResetAll: boolean;
  ResetAll?: EndpointData | null;
  ResetGroup?: EndpointData | null;
  GroupFilterIgnored: boolean;
}

const DESTINATION_CALL = 2;
const UNGROUPED = 'ungrouped';

/**
 * personnel.board (plan §7.3): the classic status table — people by group with status, time in status, staffing and the
 * staffing note (ADP userstates.note) — with collapsible groups and the admins' reset-to-available.
 */
export default function PersonnelBoardWidget({ data, size, t, ctx }: WidgetProps<PersonnelBoardWidgetData>) {
  const k = 'W.personnel.board.';
  const now = useNow(30000);
  const [collapsed, toggle] = useCollapsedGroups(ctx.instanceId);
  const [resetTarget, setResetTarget] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const groups = data.Groups ?? [];

  // Rows whose status or staffing changed since the last refresh flash briefly.
  const signatures = groups.flatMap((g) => g.People.map((p) => `${p.UserId}#${p.Status?.Id}#${p.Staffing?.Id}#${p.StatusOnUtc ?? ''}`));
  const changed = useNewKeys(signatures);
  const highlight = useMemo(() => [...changed].map((s) => s.split('#')[0]), [changed]);

  const depth = useMemo(() => {
    const byId = new Map(groups.filter((g) => g.GroupId != null).map((g) => [g.GroupId as number, g]));
    const levels = new Map<number, number>();
    for (const group of groups) {
      if (group.GroupId == null) continue;
      let level = 0;
      let parent = group.ParentGroupId;
      const seen = new Set<number>([group.GroupId]);
      while (parent != null && byId.has(parent) && !seen.has(parent) && level < 6) {
        seen.add(parent);
        level += 1;
        parent = byId.get(parent)?.ParentGroupId;
      }
      levels.set(group.GroupId, level);
    }
    return levels;
  }, [groups]);

  if (!data.TotalCount && groups.every((g) => g.People.length === 0)) {
    return <EmptyState icon="fa-users" text={t(`${k}Empty`)} onAction={data.GroupFilterIgnored ? undefined : ctx.openSettings} linkText={t(`${k}ChangeSettings`)} />;
  }

  const resettable = groups.filter((g) => g.CanReset && g.GroupId != null);

  const reset = async () => {
    if (busy) return;
    setBusy(true);
    try {
      const all = resetTarget === 'all';
      const group = groups.find((g) => String(g.GroupId) === resetTarget);
      const result = all
        ? data.ResetAll
          ? await ctx.submit(data.ResetAll.Url)
          : null
        : group && data.ResetGroup
          ? await ctx.submit(data.ResetGroup.Url, { query: { groupId: group.GroupId as number } })
          : null;
      if (result?.ok) {
        ctx.toast(all ? t(`${k}ResetAllDone`) : t(`${k}ResetGroupDone`, group?.Name || ''));
        ctx.refresh();
      } else {
        ctx.toast(t(`${k}ResetFailed`));
      }
    } finally {
      setBusy(false);
      setConfirming(false);
      setResetTarget('');
    }
  };

  const columns: TableColumn<PersonnelBoardRow>[] = [
    {
      key: 'name',
      header: t(`${k}ColName`),
      maxWidth: isWide(size) ? 220 : 150,
      render: (row) => (row.Url ? <a href={row.Url}>{row.Name}</a> : <span className="rg-pboard-name">{row.Name}</span>),
    },
    {
      key: 'status',
      header: t(`${k}ColStatus`),
      render: (row) => (
        <>
          <Chip chip={row.Status} />
          {row.StatusDestination ? (
            <span className="rg-pboard-dest">{row.StatusDestination}</span>
          ) : row.StatusDestinationType === DESTINATION_CALL && row.StatusDestinationId ? (
            <span className="rg-pboard-dest">{t(`${k}CallNumber`, row.StatusDestinationId)}</span>
          ) : null}
        </>
      ),
    },
    {
      key: 'for',
      header: t(`${k}ColFor`),
      muted: true,
      render: (row) =>
        row.StatusOnUtc ? <span title={ctx.format.dateTime(row.StatusOnUtc)}>{ctx.format.duration(minutesSince(row.StatusOnUtc, now))}</span> : '—',
    },
    { key: 'staffing', header: t(`${k}ColStaffing`), render: (row) => <Chip chip={row.Staffing} /> },
    {
      key: 'note',
      header: t(`${k}ColNote`),
      muted: true,
      maxWidth: isWide(size) ? 280 : 130,
      render: (row) => <ProtectedText value={row.StaffingNote} placeholder={t(`${k}ProtectedNote`)} />,
    },
  ];
  if (isWide(size)) {
    columns.push({ key: 'roles', header: t(`${k}ColRoles`), muted: true, maxWidth: 220, render: (row) => row.RoleNames || '' });
  }

  const label = (group: PersonnelBoardGroup) => {
    const name = group.Name || t(`${k}Ungrouped`);
    const level = group.GroupId != null ? depth.get(group.GroupId) ?? 0 : 0;
    const text = level > 0 ? `${'· '.repeat(level)}${name}` : name;
    return group.IsViewerGroup ? (
      <>
        {text} <span className="rg-tag">{t(`${k}YourGroup`)}</span>
      </>
    ) : (
      text
    );
  };

  return (
    <>
      {data.CanResetAll || resettable.length > 0 ? (
        <div className="rg-pboard-tools">
          {confirming ? (
            <>
              <span className="rg-pboard-confirm">
                {resetTarget === 'all'
                  ? t(`${k}ConfirmResetAll`)
                  : t(`${k}ConfirmResetGroup`, groups.find((g) => String(g.GroupId) === resetTarget)?.Name || '')}
              </span>
              <button type="button" className="rg-btn-xs rg-btn-primary" disabled={busy} onClick={() => void reset()}>
                {busy ? <i className="fa fa-spinner fa-spin" aria-hidden="true" /> : null} {t(`${k}ConfirmReset`)}
              </button>
              <button type="button" className="rg-btn-xs" disabled={busy} onClick={() => setConfirming(false)}>
                {t(`${k}Cancel`)}
              </button>
            </>
          ) : (
            <>
              <select className="rg-input" value={resetTarget} onChange={(event) => setResetTarget(event.target.value)} aria-label={t(`${k}ResetLabel`)}>
                <option value="">{t(`${k}ResetPlaceholder`)}</option>
                {data.CanResetAll && data.ResetAll ? <option value="all">{t(`${k}ResetAllOption`)}</option> : null}
                {resettable.map((group) => (
                  <option key={group.GroupId as number} value={String(group.GroupId)}>
                    {group.Name}
                  </option>
                ))}
              </select>
              <button type="button" className="rg-btn-xs" disabled={!resetTarget} onClick={() => setConfirming(true)}>
                {t(`${k}Reset`)}
              </button>
            </>
          )}
        </div>
      ) : null}
      <DenseTable
        columns={columns}
        groups={groups.map((group) => ({
          key: group.GroupId != null ? String(group.GroupId) : UNGROUPED,
          label: label(group),
          meta: t(
            `${k}GroupMeta`,
            group.People.length,
            group.People.filter((p) => p.Status?.Availability === Availability.Committed).length,
          ),
          rows: group.People,
        }))}
        collapsedGroups={collapsed}
        onToggleGroup={toggle}
        highlightRowKeys={highlight}
        rowKey={(row) => row.UserId}
        caption={t(`${k}Caption`)}
      />
      {data.GroupFilterIgnored ? <Note>{t(`${k}GroupFilterIgnored`)}</Note> : null}
    </>
  );
}
