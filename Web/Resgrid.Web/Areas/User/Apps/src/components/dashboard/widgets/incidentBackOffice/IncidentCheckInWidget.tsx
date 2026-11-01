import { EmptyState, ItemList, KpiGrid } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { ActivationHeader, deskUrl, type IncidentWidgetDataBase, isCompact, NoActivation, ReadOnlyNote } from './iboShared';
import './ibo.css';

/** Core/Resgrid.Model/Dashboard/Widgets/IncidentBackOffice/IncidentCheckInWidgetData.cs */
export interface IncidentCheckInWidgetData extends IncidentWidgetDataBase {
  QueueCount: number;
  AwaitingCheckIn: number;
  Drafts: number;
  Candidates: IncidentCheckInCandidate[];
}

export interface IncidentCheckInCandidate {
  Name?: string | null;
  Kind?: string | null;
  Source?: string | null;
  SeenSince?: string | null;
}

/** ibo.checkIn (plan §7.10): people waiting to be enrolled, and enrolled participants not yet checked in. */
export default function IncidentCheckInWidget({ data, size, t, ctx }: WidgetProps<IncidentCheckInWidgetData>) {
  const k = 'W.ibo.checkIn.';
  useWidgetBadge(ctx, data.HasActivation && data.QueueCount ? data.QueueCount : null);
  if (!data.HasActivation) {
    return <NoActivation t={t} />;
  }

  const enrollment = deskUrl('/User/IncidentParticipants/Enrollment', data.ActivationId);
  const checkIn = deskUrl('/User/IncidentCheckIn', data.ActivationId);
  // m keeps room for the read-only note when the add-on lapsed.
  const candidates = (data.Candidates ?? []).slice(0, isCompact(size) ? 1 : size === 'm' ? (data.ReadOnly ? 1 : 2) : undefined);

  return (
    <>
      {size !== 'm' && !isCompact(size) ? <ActivationHeader data={data} t={t} href={checkIn} /> : null}
      <KpiGrid
        columns={3}
        items={[
          { value: ctx.format.number(data.QueueCount, 0), label: t(`${k}Queue`), tone: data.QueueCount ? 'warn' : undefined, href: enrollment },
          { value: ctx.format.number(data.AwaitingCheckIn, 0), label: t(`${k}AwaitingCheckIn`), href: checkIn },
          { value: ctx.format.number(data.Drafts, 0), label: t(`${k}Drafts`) },
        ]}
      />
      {candidates.length > 0 ? (
        <ItemList
          items={candidates.map((candidate, index) => ({
            key: `${candidate.Name ?? ''}-${index}`,
            title: candidate.Name || '—',
            meta: [candidate.Kind, candidate.Source].filter(Boolean).join(' · ') || undefined,
            icon: 'fa-user-plus',
            right: candidate.SeenSince ? <span className="rg-mu">{ctx.format.ago(candidate.SeenSince)}</span> : undefined,
            href: enrollment,
          }))}
        />
      ) : !data.QueueCount && !data.AwaitingCheckIn ? (
        <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`)} />
      ) : null}
      <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
    </>
  );
}
