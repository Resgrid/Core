import type { CSSProperties, ReactNode } from 'react';

// Small building blocks shared by the primitives. Colors that carry meaning (status, severity) come from the DTO; text
// always uses the dashboard's text tokens, never a series color (plan §6.1, contract §4).

export type Tone = 'good' | 'warn' | 'bad' | 'info';

export const TONE_ICON: Record<Tone, string> = {
  good: 'fa-check-circle',
  warn: 'fa-exclamation-triangle',
  bad: 'fa-exclamation-circle',
  info: 'fa-info-circle',
};

export function toneClass(tone: Tone | null | undefined): string {
  return tone ? ` rg-tone-${tone}` : '';
}

export interface LegendItem {
  label: string;
  value?: ReactNode;
  color: string;
}

/** Color-keyed legend (used by Donut and StackBar, usable on its own). */
export function Legend({ items, className }: { items: LegendItem[]; className?: string }) {
  return (
    <ul className={`rg-legend${className ? ` ${className}` : ''}`}>
      {items.map((item, index) => (
        <li key={`${item.label}-${index}`}>
          <i style={{ background: item.color }} aria-hidden="true" />
          <span className="rg-legend-label">{item.label}</span>
          {item.value !== undefined && item.value !== null ? <b>{item.value}</b> : null}
        </li>
      ))}
    </ul>
  );
}

/** Muted footnote under a chart or list ("Median turnout 1m 48s · view as table"). */
export function Note({ children, style }: { children: ReactNode; style?: CSSProperties }) {
  return (
    <div className="rg-chart-note" style={style}>
      {children}
    </div>
  );
}

/** Small uppercase section label inside a widget body. */
export function SubHeading({ children }: { children: ReactNode }) {
  return <div className="rg-sub">{children}</div>;
}

export interface EmptyStateProps {
  /** Font Awesome 4.7 class, e.g. "fa-check-circle-o". */
  icon?: string;
  text: string;
  /** Optional primary link ("Create a call"). */
  linkText?: string;
  href?: string;
  onAction?: () => void;
}

/** Icon + one sentence + optional primary link (plan §6.4 Empty). */
export function EmptyState({ icon = 'fa-inbox', text, linkText, href, onAction }: EmptyStateProps) {
  return (
    <div className="rg-empty">
      <i className={`fa ${icon}`} aria-hidden="true" />
      <span>{text}</span>
      {linkText && href ? <a href={href}>{linkText}</a> : null}
      {linkText && !href && onAction ? (
        <button type="button" className="rg-btn-xs" onClick={onAction}>
          {linkText}
        </button>
      ) : null}
    </div>
  );
}
