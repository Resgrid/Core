import { useMemo } from 'react';
import { EmptyState, KpiGrid, MiniMap, Note, type MiniMapMarker } from '../../primitives';
import type { WidgetProps } from '../types';
import { toneIf } from '../readiness/readinessShared';
import './RecordsHydrantsWidget.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsHydrantsWidgetData.cs */
export interface RecordsHydrantItem {
  HydrantId: string;
  Number?: string | null;
  /** RmsHydrantType. */
  Type: number;
  Latitude: number;
  Longitude: number;
  OutOfServiceSince?: string | null;
}

export interface RecordsHydrantsWidgetData {
  OutOfService: number;
  TestDue: number;
  OutOfServiceHydrants: RecordsHydrantItem[];
}

const HYDRANTS_URL = '/User/RecordHydrants';

/** records.hydrants (plan §7.8): out-of-service and test-due counts with the out-of-service hydrants on a map. */
export default function RecordsHydrantsWidget({ data, t, ctx }: WidgetProps<RecordsHydrantsWidgetData>) {
  const k = 'W.records.hydrants.';
  const hydrants = data.OutOfServiceHydrants ?? [];
  const markers = useMemo<MiniMapMarker[]>(
    () =>
      hydrants
        .filter((hydrant) => Number.isFinite(hydrant.Latitude) && Number.isFinite(hydrant.Longitude) && (hydrant.Latitude !== 0 || hydrant.Longitude !== 0))
        .map((hydrant) => ({
          id: hydrant.HydrantId,
          lat: hydrant.Latitude,
          lng: hydrant.Longitude,
          radius: 6,
          tooltip: hydrant.OutOfServiceSince
            ? t(`${k}MarkerSince`, hydrant.Number || t(`${k}NoNumber`), ctx.format.date(hydrant.OutOfServiceSince))
            : t(`${k}Marker`, hydrant.Number || t(`${k}NoNumber`)),
        })),
    [hydrants, t, ctx.format, k],
  );

  const kpis = (
    <KpiGrid
      columns={2}
      items={[
        { value: ctx.format.number(data.OutOfService, 0), label: t(`${k}OutOfService`), tone: data.OutOfService > 0 ? 'bad' : 'good', href: HYDRANTS_URL },
        { value: ctx.format.number(data.TestDue, 0), label: t(`${k}TestDue`), tone: toneIf(data.TestDue, 'warn'), href: HYDRANTS_URL },
      ]}
    />
  );

  if (!data.OutOfService) {
    return (
      <>
        {kpis}
        <EmptyState icon="fa-tint" text={t(`${k}AllInService`)} linkText={t(`${k}OpenHydrants`)} href={HYDRANTS_URL} />
      </>
    );
  }

  return (
    <div className="rg-records-hydrants-body">
      {kpis}
      {markers.length ? (
        <div className="rg-records-hydrants-map">
          <MiniMap markers={markers} ariaLabel={t(`${k}MapAria`, markers.length)} />
        </div>
      ) : (
        <Note>{t(`${k}NoPositions`)}</Note>
      )}
      {data.OutOfService > markers.length && markers.length ? <Note>{t(`${k}MappedOf`, markers.length, data.OutOfService)}</Note> : null}
    </div>
  );
}
