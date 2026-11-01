import { useState } from 'react';
import { EmptyState, ItemList, SubHeading } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { bffPath, type EndpointData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/UnitsRoutesWidgetData.cs */
export interface UnitsRouteInstance {
  RouteInstanceId: string;
  RoutePlanId?: string | null;
  PlanName?: string | null;
  PlanColor?: string | null;
  UnitId: number;
  UnitName?: string | null;
  /** 1 in progress, 5 paused. */
  Status: number;
  StartedOnUtc?: string | null;
  StopsCompleted: number;
  StopsTotal: number;
  DeviationCount: number;
  Url?: string | null;
}

export interface UnitsRouteDeviation {
  RouteDeviationId: string;
  RouteInstanceId?: string | null;
  PlanName?: string | null;
  UnitName?: string | null;
  DetectedOnUtc: string;
  DeviationType: number;
  DistanceMeters: number;
  Url?: string | null;
}

export interface UnitsRoutesWidgetData {
  ActiveRoutes: UnitsRouteInstance[];
  Deviations: UnitsRouteDeviation[];
  ActiveRouteCount: number;
  UnacknowledgedDeviationCount: number;
  AcknowledgeDeviation?: EndpointData | null;
  ActiveRoutesUrl?: string | null;
}

const PAUSED = 5;

/** units.routes (plan §7.2): routes in progress and the deviations nobody has acknowledged yet. */
export default function UnitsRoutesWidget({ data, t, ctx }: WidgetProps<UnitsRoutesWidgetData>) {
  const k = 'W.units.routes.';
  const [busy, setBusy] = useState<string | null>(null);
  useWidgetBadge(ctx, data.UnacknowledgedDeviationCount > 0 ? data.UnacknowledgedDeviationCount : null);

  const routes = data.ActiveRoutes ?? [];
  const deviations = data.Deviations ?? [];
  const listUrl = data.ActiveRoutesUrl ?? '/User/Routes/ActiveRoutes';
  if (routes.length === 0 && deviations.length === 0) {
    return <EmptyState icon="fa-map-signs" text={t(`${k}Empty`)} linkText={t(`${k}OpenRoutes`)} href={listUrl} />;
  }

  const distance = (meters: number) => (meters >= 1000 ? t(`${k}Kilometres`, ctx.format.number(meters / 1000, 1)) : t(`${k}Metres`, ctx.format.number(meters, 0)));

  const acknowledge = async (deviation: UnitsRouteDeviation) => {
    if (!data.AcknowledgeDeviation?.Available || busy !== null) return;
    setBusy(deviation.RouteDeviationId);
    try {
      await ctx.api(bffPath(data.AcknowledgeDeviation.Url, deviation.RouteDeviationId), { method: 'POST' });
      ctx.toast(t(`${k}Acknowledged`));
      ctx.refresh();
    } catch {
      ctx.toast(t(`${k}AcknowledgeFailed`));
    } finally {
      setBusy(null);
    }
  };

  return (
    <>
      {deviations.length > 0 ? (
        <>
          <SubHeading>{t(`${k}Deviations`, data.UnacknowledgedDeviationCount)}</SubHeading>
          <ItemList
            items={deviations.map((deviation) => ({
              key: deviation.RouteDeviationId,
              icon: 'fa-random',
              tone: 'warn' as const,
              title: t(`${k}OffRoute`, deviation.UnitName || deviation.PlanName || t(`${k}Route`), distance(deviation.DistanceMeters)),
              href: deviation.Url ?? undefined,
              meta: [deviation.PlanName, ctx.format.ago(deviation.DetectedOnUtc)].filter(Boolean).join(' · '),
              right: data.AcknowledgeDeviation?.Available ? (
                <button type="button" className="rg-btn-xs" disabled={busy !== null} onClick={() => void acknowledge(deviation)}>
                  {busy === deviation.RouteDeviationId ? <i className="fa fa-spinner fa-spin" aria-hidden="true" /> : null} {t(`${k}Acknowledge`)}
                </button>
              ) : undefined,
            }))}
          />
        </>
      ) : null}
      {routes.length > 0 ? (
        <>
          <SubHeading>{t(`${k}InProgress`, data.ActiveRouteCount)}</SubHeading>
          <ItemList
            items={routes.map((route) => ({
              key: route.RouteInstanceId,
              icon: route.Status === PAUSED ? 'fa-pause-circle-o' : 'fa-map-signs',
              accentColor: route.PlanColor || undefined,
              title: [route.UnitName, route.PlanName].filter(Boolean).join(' · ') || t(`${k}Route`),
              href: route.Url ?? undefined,
              meta: [
                route.Status === PAUSED ? t(`${k}Paused`) : null,
                t(`${k}Stops`, route.StopsCompleted, route.StopsTotal),
                route.StartedOnUtc ? t(`${k}Started`, ctx.format.time(route.StartedOnUtc)) : null,
              ]
                .filter(Boolean)
                .join(' · '),
              right: route.DeviationCount > 0 ? <span className="rg-tag">{t(`${k}DeviationCount`, route.DeviationCount)}</span> : undefined,
            }))}
          />
        </>
      ) : null}
    </>
  );
}
