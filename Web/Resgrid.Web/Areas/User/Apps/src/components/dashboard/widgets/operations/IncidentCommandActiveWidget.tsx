import { EmptyState, ItemList } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { isCompact, useNow } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/IncidentCommandActiveWidgetData.cs */
export interface IncidentCommandActiveRow {
  IncidentCommandId: string;
  CallId: number;
  Name?: string | null;
  CallName?: string | null;
  CallNumber?: string | null;
  CallAddress?: string | null;
  CommanderName?: string | null;
  EstablishedOnUtc: string;
  AssignedUnitCount: number;
  AssignedPersonnelCount: number;
  CallUrl?: string | null;
}

export interface IncidentCommandActiveWidgetData {
  TotalCount: number;
  Commands: IncidentCommandActiveRow[];
}

/** incidentCommand.active (plan §7.1): open incident commands with their commander and assigned resources. */
export default function IncidentCommandActiveWidget({ data, size, t, ctx }: WidgetProps<IncidentCommandActiveWidgetData>) {
  const k = 'W.incidentCommand.active.';
  useNow(60000);
  useWidgetBadge(ctx, data.TotalCount > 0 ? data.TotalCount : null);

  const commands = data.Commands ?? [];
  if (commands.length === 0) {
    return <EmptyState icon="fa-sitemap" text={t(`${k}Empty`)} />;
  }

  return (
    <ItemList
      items={commands.map((command) => {
        const parts = [
          command.CommanderName ? t(`${k}Commander`, command.CommanderName) : t(`${k}NoCommander`),
          t(`${k}Assigned`, command.AssignedUnitCount, command.AssignedPersonnelCount),
        ];
        if (!isCompact(size) && command.CallAddress) parts.push(command.CallAddress);
        return {
          key: command.IncidentCommandId,
          icon: 'fa-sitemap',
          title: (
            <>
              {command.Name || command.CallName || t(`${k}Untitled`)}
              {command.CallNumber ? <span className="rg-ops-num">{command.CallNumber}</span> : null}
            </>
          ),
          href: command.CallUrl ?? undefined,
          meta: parts.join(' · '),
          right: <span title={t(`${k}EstablishedAt`, ctx.format.dateTime(command.EstablishedOnUtc))}>{ctx.format.ago(command.EstablishedOnUtc)}</span>,
        };
      })}
    />
  );
}
