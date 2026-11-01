import { EmptyState, HeroNumber, Note } from '../../primitives';
import { has } from '../../i18n';
import { calendarDayLabel } from '../planning/planningTime';
import type { Translate, WidgetProps } from '../types';
import './awarenessWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Awareness/WeatherCurrentWidgetData.cs */
export interface WeatherCurrentWidgetData {
  Available: boolean;
  Units: 'imperial' | 'metric' | string;
  LocationName?: string | null;
  Latitude: number;
  Longitude: number;
  Current?: {
    Temperature: number;
    FeelsLike: number;
    Condition: string;
    Description?: string | null;
    Icon: string;
    Humidity: number;
    WindSpeed: number;
    WindDirection?: number | null;
  } | null;
  Forecast: Array<{ Day: string; DayOfWeek: number; High: number; Low: number; Condition: string; Icon: string; PrecipitationChance: number }>;
  ObservedUtc?: string | null;
  FetchedUtc?: string | null;
}

/** OpenWeather condition group → Font Awesome 4.7 glyph (decorative; the label carries the meaning). */
export function weatherIcon(condition: string | null | undefined, iconCode?: string | null): string {
  const night = typeof iconCode === 'string' && iconCode.endsWith('n');
  switch (condition) {
    case 'Clear':
      return night ? 'fa-moon-o' : 'fa-sun-o';
    case 'Rain':
    case 'Drizzle':
      return 'fa-tint';
    case 'Thunderstorm':
      return 'fa-bolt';
    case 'Snow':
      return 'fa-snowflake-o';
    default:
      return 'fa-cloud';
  }
}

export function conditionLabel(t: Translate, condition: string | null | undefined): string {
  const key = `W.weather.current.Condition.${condition ?? 'Unknown'}`;
  return has(key) ? t(key) : condition || t('W.weather.current.Condition.Unknown');
}

const COMPASS = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];

/** weather.current (plan §7.4): current conditions for the department center and a short outlook (m). */
export default function WeatherCurrentWidget({ data, size, t, ctx }: WidgetProps<WeatherCurrentWidgetData>) {
  const current = data.Current;
  if (!data.Available || !current) {
    return <EmptyState icon="fa-cloud" text={t('W.weather.current.Unavailable')} />;
  }

  const metric = data.Units === 'metric';
  const tempUnit = metric ? '°C' : '°F';
  const windUnit = metric ? 'm/s' : 'mph';
  const direction =
    typeof current.WindDirection === 'number' ? t(`W.weather.current.Compass.${COMPASS[Math.round((((current.WindDirection % 360) + 360) % 360) / 45) % 8]}`) : '';
  const wind = `${t('W.weather.current.Wind')} ${direction ? `${direction} ` : ''}${ctx.format.number(current.WindSpeed, 0)} ${windUnit}`;
  const humidity = `${t('W.weather.current.Humidity')} ${ctx.format.percent(current.Humidity)}`;
  // s shows today's conditions only; m adds the outlook (today and the next three days).
  const outlook = size === 's' ? [] : data.Forecast.slice(0, 4);

  return (
    <>
      <HeroNumber
        icon={weatherIcon(current.Condition, current.Icon)}
        value={`${ctx.format.number(current.Temperature, 0)}${tempUnit}`}
        label={
          <span title={current.Description ?? undefined}>
            {conditionLabel(t, current.Condition)} · {t('W.weather.current.FeelsLike', `${ctx.format.number(current.FeelsLike, 0)}°`)}
            <br />
            {wind} · {humidity}
          </span>
        }
      />
      {outlook.length > 0 ? (
        <ul className="rg-weather-days">
          {outlook.map((day) => (
            <li key={day.Day} title={`${conditionLabel(t, day.Condition)} · ${t('W.weather.current.Precipitation', day.PrecipitationChance)}`}>
              <span className="rg-weather-day">{calendarDayLabel(day.Day, t, ctx.format.timeZone, 'W.weather.current.Today', 'W.weather.current.Tomorrow')}</span>
              <i className={`fa ${weatherIcon(day.Condition, day.Icon)}`} aria-hidden="true" />
              <span className="rg-num">
                <b>{ctx.format.number(day.High, 0)}°</b> {ctx.format.number(day.Low, 0)}°
              </span>
            </li>
          ))}
        </ul>
      ) : null}
      <Note style={{ marginTop: 8 }}>
        {data.LocationName ? `${data.LocationName} · ` : ''}
        {t('W.weather.current.UpdatedAt', ctx.format.time(data.FetchedUtc ?? data.ObservedUtc))}
      </Note>
    </>
  );
}
