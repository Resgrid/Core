import type { ReactNode } from 'react';
import { Legend, toneClass, type LegendItem, type Tone } from './basic';

// Number-first primitives: KPI grid, hero number, sparkline, donut, stacked bar and bar list. Charts are plain SVG
// with a text alternative (aria-label) and per-point titles; nothing here bundles a chart library.

export interface KpiItem {
  value: ReactNode;
  label: string;
  /** good / warn / bad add the matching icon before the label. */
  tone?: Tone;
  href?: string;
  title?: string;
}

export function KpiGrid({ items, columns }: { items: KpiItem[]; columns?: number }) {
  return (
    <div className="rg-kpis" style={columns ? { gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` } : undefined}>
      {items.map((item, index) => {
        const content = (
          <>
            <div className="rg-kpi-v">{item.value}</div>
            <div className="rg-kpi-l">{item.label}</div>
          </>
        );
        const className = `rg-kpi${toneClass(item.tone)}`;
        return item.href ? (
          <a key={index} className={className} href={item.href} title={item.title}>
            {content}
          </a>
        ) : (
          <div key={index} className={className} title={item.title}>
            {content}
          </div>
        );
      })}
    </div>
  );
}

export function HeroNumber(props: { value: ReactNode; label?: ReactNode; icon?: string; iconColor?: string; size?: 'md' | 'lg' }) {
  return (
    <div className={`rg-hero${props.size === 'md' ? ' rg-hero-md' : ''}`}>
      {props.icon ? <i className={`fa ${props.icon} rg-hero-icon`} style={props.iconColor ? { color: props.iconColor } : undefined} aria-hidden="true" /> : null}
      <span className="rg-hero-v">{props.value}</span>
      {props.label ? <span className="rg-hero-l">{props.label}</span> : null}
    </div>
  );
}

export interface SparklineProps {
  values: number[];
  /** One label per value for the hover titles ("Oct 31"). */
  labels?: string[];
  /** Text alternative, e.g. "Calls per day, last 7 days: 21, 18, 25…". */
  ariaLabel: string;
  /** Line color (default the primary token). */
  color?: string;
  height?: number;
  formatValue?: (value: number, index: number) => string;
}

export function Sparkline({ values, labels, ariaLabel, color = 'var(--rg-primary)', height = 44, formatValue }: SparklineProps) {
  if (values.length === 0) {
    return null;
  }
  const width = 240;
  const max = Math.max(...values);
  const min = Math.min(...values);
  const span = max - min || 1;
  const step = values.length > 1 ? width / (values.length - 1) : width;
  const points = values.map((value, index) => [values.length > 1 ? index * step : width / 2, height - 4 - ((value - min) / span) * (height - 10)] as const);
  const line = points.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' ');
  const last = points[points.length - 1];

  return (
    <svg className="rg-spark" viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" role="img" aria-label={ariaLabel} style={{ height }}>
      <polygon points={`0,${height} ${line} ${width},${height}`} style={{ fill: color, fillOpacity: 0.1 }} />
      <polyline points={line} style={{ fill: 'none', stroke: color }} strokeWidth={2} vectorEffect="non-scaling-stroke" strokeLinejoin="round" />
      <circle cx={last[0]} cy={last[1]} r={4} style={{ fill: color }} stroke="#fff" strokeWidth={2} vectorEffect="non-scaling-stroke" />
      {points.map(([x], index) => (
        <rect key={index} x={(x - step / 2).toFixed(1)} y={0} width={step.toFixed(1)} height={height} fill="transparent">
          <title>{`${labels?.[index] ?? index + 1}: ${formatValue ? formatValue(values[index], index) : values[index]}`}</title>
        </rect>
      ))}
    </svg>
  );
}

export interface PartItem {
  label: string;
  value: number;
  /** From the DTO (department state color, priority color). */
  color: string;
}

export function Donut(props: { parts: PartItem[]; ariaLabel: string; centerLabel?: string; centerValue?: ReactNode; size?: number; showLegend?: boolean }) {
  const { parts, size = 104, showLegend = true } = props;
  const total = parts.reduce((sum, part) => sum + Math.max(0, part.value), 0);
  const radius = 38;
  const circumference = 2 * Math.PI * radius;
  const gap = parts.filter((part) => part.value > 0).length > 1 ? 2.5 : 0;
  let offset = 0;

  return (
    <div className="rg-donut-wrap">
      <svg viewBox="0 0 100 100" width={size} height={size} role="img" aria-label={props.ariaLabel}>
        {total === 0 ? <circle r={radius} cx={50} cy={50} fill="none" stroke="#eef1f3" strokeWidth={14} /> : null}
        {parts
          .filter((part) => part.value > 0)
          .map((part, index) => {
            const length = (part.value / total) * circumference;
            const dash = Math.max(0, length - gap);
            const segment = (
              <circle
                key={`${part.label}-${index}`}
                r={radius}
                cx={50}
                cy={50}
                fill="none"
                style={{ stroke: part.color }}
                strokeWidth={14}
                strokeDasharray={`${dash.toFixed(2)} ${(circumference - dash).toFixed(2)}`}
                strokeDashoffset={(-offset).toFixed(2)}
                transform="rotate(-90 50 50)"
              >
                <title>{`${part.label}: ${part.value}`}</title>
              </circle>
            );
            offset += length;
            return segment;
          })}
        <text x={50} y={props.centerLabel ? 49 : 56} textAnchor="middle" fontSize={20} style={{ fill: 'var(--rg-heading)' }}>
          {props.centerValue ?? total}
        </text>
        {props.centerLabel ? (
          <text x={50} y={63} textAnchor="middle" fontSize={9} style={{ fill: 'var(--rg-muted)' }}>
            {props.centerLabel}
          </text>
        ) : null}
      </svg>
      {showLegend ? <Legend items={parts.map<LegendItem>((part) => ({ label: part.label, value: part.value, color: part.color }))} /> : null}
    </div>
  );
}

export function StackBar(props: { parts: PartItem[]; ariaLabel: string; showLegend?: boolean }) {
  const { parts, showLegend = true } = props;
  return (
    <>
      <div className="rg-stack" role="img" aria-label={`${props.ariaLabel}: ${parts.map((part) => `${part.label} ${part.value}`).join(', ')}`}>
        {parts
          .filter((part) => part.value > 0)
          .map((part, index) => (
            <span key={`${part.label}-${index}`} title={`${part.label}: ${part.value}`} style={{ flex: part.value, background: part.color }} />
          ))}
      </div>
      {showLegend ? <Legend items={parts.map((part) => ({ label: part.label, value: part.value, color: part.color }))} /> : null}
    </>
  );
}

export interface BarItem {
  label: string;
  value: number;
  /** Shown instead of the formatted value ("6 / 8"). */
  display?: string;
  /** Fill color (default the primary token). */
  color?: string;
  /** warn / bad add the matching icon before the label. */
  tone?: Tone;
  href?: string;
}

export function BarList(props: { items: BarItem[]; max?: number; format?: (value: number) => string }) {
  const max = props.max ?? Math.max(1, ...props.items.map((item) => item.value));
  return (
    <div className="rg-bars">
      {props.items.map((item, index) => {
        const percent = max > 0 ? Math.max(item.value > 0 ? 2 : 0, Math.min(100, Math.round((item.value / max) * 100))) : 0;
        const shown = item.display ?? (props.format ? props.format(item.value) : String(item.value));
        const label = item.href ? <a href={item.href}>{item.label}</a> : item.label;
        return (
          <div key={`${item.label}-${index}`} className={`rg-bar${toneClass(item.tone)}`} title={`${item.label}: ${shown}`}>
            <span className="rg-bar-lbl">{label}</span>
            <span className="rg-bar-trk" aria-hidden="true">
              <span className="rg-bar-fill" style={{ width: `${percent}%`, background: item.color }} />
            </span>
            <span className="rg-bar-val">{shown}</span>
          </div>
        );
      })}
    </div>
  );
}
