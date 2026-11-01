import { EmptyState, ItemList, Legend, StatusChip } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Awareness/WeatherAlertsWidgetData.cs */
export interface WeatherAlertsWidgetData {
  TotalCount: number;
  Severities: Array<{ Severity: number; SeverityKey: string; Color: string; Count: number }>;
  Items: Array<{
    WeatherAlertId: string;
    Event?: string | null;
    Headline?: string | null;
    Severity: number;
    SeverityKey: string;
    Color: string;
    Urgency: number;
    Certainty: number;
    AreaDescription?: string | null;
    Sender?: string | null;
    EffectiveUtc: string;
    OnsetUtc?: string | null;
    ExpiresUtc?: string | null;
    Url: string;
  }>;
}

/** weather.alerts (plan §7.4): active alerts, most severe first, with severity counts at larger sizes. */
export default function WeatherAlertsWidget({ data, size, t, ctx }: WidgetProps<WeatherAlertsWidgetData>) {
  useWidgetBadge(ctx, data.TotalCount > 0 ? data.TotalCount : null);

  if (data.TotalCount === 0) {
    return <EmptyState icon="fa-sun-o" text={t('W.weather.alerts.Empty')} />;
  }

  const shown = data.Items;
  const hiddenByFilter = data.TotalCount - shown.length;

  return (
    <>
      {size === 'l' ? (
        <Legend
          items={data.Severities.filter((s) => s.Count > 0).map((s) => ({ label: t(`W.weather.alerts.Severity.${s.SeverityKey}`), value: s.Count, color: s.Color }))}
        />
      ) : null}
      <ItemList
        items={shown.map((alert) => ({
          key: alert.WeatherAlertId,
          title: (
            <>
              <StatusChip label={t(`W.weather.alerts.Severity.${alert.SeverityKey}`)} color={alert.Color} /> {alert.Event}
            </>
          ),
          meta: [
            alert.ExpiresUtc ? t('W.weather.alerts.Expires', ctx.format.dateTime(alert.ExpiresUtc)) : null,
            size === 'l' ? alert.AreaDescription : null,
            alert.Sender,
          ]
            .filter(Boolean)
            .join(' · '),
          href: alert.Url,
        }))}
      />
      {hiddenByFilter > 0 ? <div className="rg-chart-note">{t('W.weather.alerts.More', hiddenByFilter)}</div> : null}
    </>
  );
}
