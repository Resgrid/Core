import { useEffect, useMemo, useState } from 'react';
import { safeTimeZone, uiLocale } from '../../i18n';
import type { WidgetProps } from '../types';
import './awarenessWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Awareness/ClockWidgetData.cs */
export interface ClockWidgetData {
  /** IANA zone ("America/Los_Angeles"). */
  TimeZoneId: string;
  DepartmentTimeZone?: string | null;
}

function setting<T>(settings: Record<string, unknown>, name: string, fallback: T): T {
  const value = settings[name];
  return value === undefined || value === null ? fallback : (value as T);
}

/** general.clock (plan §7.4): department time large, UTC and the viewer's own time underneath (Big Board Time widget). */
export default function ClockWidget({ data, instance, size, t }: WidgetProps<ClockWidgetData>) {
  const settings = instance.Settings ?? {};
  const format = setting<string>(settings, 'format', 'auto');
  const showSeconds = setting<boolean>(settings, 'showSeconds', false);
  const showUtc = setting<boolean>(settings, 'showUtc', true);
  const showLocal = setting<boolean>(settings, 'showLocal', true);

  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), showSeconds ? 1000 : 10000);
    return () => window.clearInterval(timer);
  }, [showSeconds]);

  const zone = safeTimeZone(data.TimeZoneId) ?? 'UTC';
  const formats = useMemo(() => {
    const hour12 = format === '12h' ? true : format === '24h' ? false : undefined;
    const locale = uiLocale();
    const make = (timeZone: string | undefined, seconds: boolean) =>
      new Intl.DateTimeFormat(locale, { timeZone, hour: '2-digit', minute: '2-digit', second: seconds ? '2-digit' : undefined, hour12 });
    return {
      department: make(zone, showSeconds),
      utc: make('UTC', false),
      local: make(undefined, false),
      date: new Intl.DateTimeFormat(locale, { timeZone: zone, weekday: 'short', month: 'short', day: 'numeric' }),
    };
  }, [format, showSeconds, zone]);

  const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const localDiffers = showLocal && browserZone !== zone;
  const compact = size === 'xs';

  return (
    <div className={`rg-clock${compact ? ' rg-clock-xs' : ''}`}>
      <div className="rg-clock-time rg-num" aria-live="off">
        {formats.department.format(now)}
      </div>
      {compact ? null : (
        <div className="rg-clock-meta">
          <span>{formats.date.format(now)} · </span>
          <span title={data.DepartmentTimeZone ?? undefined}>{t('W.general.clock.Department')}</span>
        </div>
      )}
      {!compact && (showUtc || localDiffers) ? (
        <div className="rg-clock-others">
          {showUtc ? (
            <span>
              {t('W.general.clock.Utc')} <b className="rg-num">{formats.utc.format(now)}</b>
            </span>
          ) : null}
          {localDiffers ? (
            <span>
              {t('W.general.clock.Local')} <b className="rg-num">{formats.local.format(now)}</b>
            </span>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
