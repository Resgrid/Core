import { categoryLabel, formatNumber, t } from './vizI18n';
import type { VizDefinition, VizResult } from './vizTypes';

interface VizSummaryProps {
  definition: VizDefinition;
  result: VizResult;
}

// Monday first; values are .NET DayOfWeek (0 = Sunday).
const dayOrder = [1, 2, 3, 4, 5, 6, 0];

function cellColor(value: number, max: number): string {
  if (value === 0 || max === 0) {
    return 'transparent';
  }
  // Same hue as the heat ramp's hot end, opacity by share of the busiest hour.
  const share = Math.sqrt(value / max);
  return `rgba(215, 25, 28, ${(0.12 + 0.78 * share).toFixed(2)})`;
}

export default function VizSummary(props: VizSummaryProps) {
  const { result } = props;
  const summary = result.Summary;
  const grid = summary.ByWeekdayHour ?? [];
  const max = grid.reduce((highest, row) => row.reduce((h, v) => (v > h ? v : h), highest), 0);
  const topMax = summary.TopCallTypes.reduce((highest, c) => (c.Count > highest ? c.Count : highest), 0);
  const notices = Array.from(new Set(result.Layers.flatMap((layer) => layer.Notices.map((notice) => `${layer.Source}|${notice}`))));
  // Department-local values without a zone suffix: Date reads them as wall-clock times, so the dates match the department's.
  const start = new Date(result.StartLocal).toLocaleDateString();
  const end = new Date(result.EndLocal).toLocaleDateString();

  return (
    <div className="rg-viz__summary">
      <p className="rg-viz__summary-window">
        {result.Call ? t('SummaryCall', [result.Call.Number, result.Call.Name].filter(Boolean).join(' · ')) : t('SummaryWindow', start, end)}
      </p>

      {notices.length > 0 && (
        <ul className="rg-viz__notices" role="status">
          {notices.map((entry) => {
            const [source, notice] = entry.split('|');
            return <li key={entry}>{t(`Notice_${notice}`, t(`Source_${source}`))}</li>;
          })}
        </ul>
      )}
      {result.Truncated && <p className="rg-viz__notices">{t('NoticeCallsTruncated')}</p>}

      <div className="rg-viz__kpis">
        <div className="rg-viz__kpi">
          <span className="rg-viz__kpi-value">{formatNumber(summary.Calls, true)}</span>
          <span className="rg-viz__kpi-label">{t('KpiCalls')}</span>
        </div>
        <div className="rg-viz__kpi">
          <span className="rg-viz__kpi-value">{summary.Calls > 0 ? `${Math.round((summary.CallsWithLocation / summary.Calls) * 100)}%` : '—'}</span>
          <span className="rg-viz__kpi-label">{t('KpiMapped')}</span>
        </div>
        <div className="rg-viz__kpi">
          <span className="rg-viz__kpi-value">{summary.MedianResponseMinutes !== null ? `${formatNumber(summary.MedianResponseMinutes)} ${t('UnitMinutes')}` : '—'}</span>
          <span className="rg-viz__kpi-label">{t('KpiMedianResponse')}</span>
        </div>
        <div className="rg-viz__kpi">
          <span className="rg-viz__kpi-value">{summary.P90ResponseMinutes !== null ? `${formatNumber(summary.P90ResponseMinutes)} ${t('UnitMinutes')}` : '—'}</span>
          <span className="rg-viz__kpi-label">{t('KpiP90Response')}</span>
        </div>
      </div>
      {summary.MedianResponseMinutes === null && <p className="rg-viz__hint">{t('KpiResponseHint')}</p>}

      <div className="rg-viz__summary-grid">
        <div>
          <h4 className="rg-viz__subtitle">{t('SummaryWhen')}</h4>
          <div className="rg-viz__table-wrap">
            <table className="rg-viz__hours">
              <caption className="rg-viz__sr-only">{t('SummaryWhen')}</caption>
              <thead>
                <tr>
                  <th scope="col"><span className="rg-viz__sr-only">{t('FilterDays')}</span></th>
                  {Array.from({ length: 24 }, (_, hour) => (
                    <th key={hour} scope="col">{hour % 3 === 0 ? String(hour).padStart(2, '0') : ''}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {dayOrder.map((day) => (
                  <tr key={day}>
                    <th scope="row">{t(`DayShort_${day}`)}</th>
                    {(grid[day] ?? []).map((value, hour) => (
                      <td key={hour} style={{ backgroundColor: cellColor(value, max) }} title={`${t(`Day_${day}`)} ${String(hour).padStart(2, '0')}:00 — ${value}`}>
                        <span className="rg-viz__sr-only">{value}</span>
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        <div>
          <h4 className="rg-viz__subtitle">{t('SummaryTopTypes')}</h4>
          {summary.TopCallTypes.length === 0 ? (
            <p className="rg-viz__hint">{t('LegendEmpty')}</p>
          ) : (
            <ul className="rg-viz__bars">
              {summary.TopCallTypes.map((type) => (
                <li key={type.Key}>
                  <span className="rg-viz__bar-label">{categoryLabel(type, 'callType', props.definition.Options)}</span>
                  <span className="rg-viz__bar-track">
                    <span className="rg-viz__bar" style={{ width: `${topMax > 0 ? (type.Count / topMax) * 100 : 0}%` }} />
                  </span>
                  <span className="rg-viz__bar-value">{formatNumber(type.Count, true)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}
