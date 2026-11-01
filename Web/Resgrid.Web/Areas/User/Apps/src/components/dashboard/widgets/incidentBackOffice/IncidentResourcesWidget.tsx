import { BarList, EmptyState, KpiGrid, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import { ActivationHeader, deskUrl, type IncidentKeyCount, type IncidentWidgetDataBase, NoActivation, ReadOnlyNote } from './iboShared';
import './ibo.css';

/** Core/Resgrid.Model/Dashboard/Widgets/IncidentBackOffice/IncidentResourcesWidgetData.cs */
export interface IncidentResourcesWidgetData extends IncidentWidgetDataBase {
  Total: number;
  OnIncident: number;
  Inbound: number;
  PersonnelOnIncident: number;
  ByKind: IncidentKeyCount[];
  ByStatus: IncidentKeyCount[];
}

/** ibo.resources (plan §7.10): an incident's resources by kind and support status. */
export default function IncidentResourcesWidget({ data, size, t, ctx }: WidgetProps<IncidentResourcesWidgetData>) {
  const k = 'W.ibo.resources.';
  if (!data.HasActivation) {
    return <NoActivation t={t} />;
  }

  const href = deskUrl('/User/IncidentOperations', data.ActivationId);
  return (
    <>
      <ActivationHeader data={data} t={t} href={href} />
      {!data.Total ? (
        <EmptyState icon="fa-truck" text={t(`${k}Empty`)} linkText={t(`${k}OpenDesk`)} href={href} />
      ) : (
        <>
          <KpiGrid
            columns={4}
            items={[
              { value: ctx.format.number(data.Total, 0), label: t(`${k}Total`), href },
              { value: ctx.format.number(data.OnIncident, 0), label: t(`${k}OnIncident`) },
              { value: ctx.format.number(data.Inbound, 0), label: t(`${k}Inbound`) },
              { value: ctx.format.number(data.PersonnelOnIncident, 0), label: t(`${k}Personnel`) },
            ]}
          />
          {size === 'm' ? (
            <>
              <SubHeading>{t(`${k}ByKind`)}</SubHeading>
              <BarList items={(data.ByKind ?? []).slice(0, 2).map((row) => ({ label: t(`W.ibo.ResourceKind.${row.Key}`), value: row.Count }))} />
            </>
          ) : (
            // l: kind and support status side by side.
            <div className="rg-ibo-cols">
              <div>
                <SubHeading>{t(`${k}ByKind`)}</SubHeading>
                <BarList items={(data.ByKind ?? []).slice(0, 6).map((row) => ({ label: t(`W.ibo.ResourceKind.${row.Key}`), value: row.Count }))} />
              </div>
              <div>
                <SubHeading>{t(`${k}ByStatus`)}</SubHeading>
                <BarList items={(data.ByStatus ?? []).slice(0, 6).map((row) => ({ label: t(`W.ibo.SupportStatus.${row.Key}`), value: row.Count }))} />
              </div>
            </div>
          )}
        </>
      )}
      <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
    </>
  );
}
