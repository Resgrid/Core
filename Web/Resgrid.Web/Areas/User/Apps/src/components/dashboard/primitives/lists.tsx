import { Fragment, type CSSProperties, type ReactNode } from 'react';
import { usePrimitiveRuntime } from '../runtime';
import type { DashboardProtectedText } from '../types';
import { TONE_ICON, type Tone } from './basic';

// List-shaped primitives: status chip, item list, agenda, dense table and protected text.

export interface StatusChipProps {
  label: string;
  /** The state's color from the DTO (department custom state ButtonColor, built-in state color). */
  color?: string | null;
  /** "dot" (default): colored dot + heading-colored text. "solid": the department's button look (color + textColor). */
  variant?: 'dot' | 'solid';
  textColor?: string | null;
  title?: string;
}

export function StatusChip({ label, color, variant = 'dot', textColor, title }: StatusChipProps) {
  if (variant === 'solid') {
    return (
      <span className="rg-chip rg-chip-solid" title={title} style={{ background: color || 'var(--rg-text)', color: textColor || '#fff' }}>
        {label}
      </span>
    );
  }
  return (
    <span className="rg-chip" title={title}>
      <i style={{ background: color || 'var(--rg-muted)' }} aria-hidden="true" />
      {label}
    </span>
  );
}

export interface ListItem {
  key?: string;
  title: ReactNode;
  meta?: ReactNode;
  /** Right-aligned cell: an age, a count, a small button. */
  right?: ReactNode;
  /** Font Awesome 4.7 class shown before the text. */
  icon?: string;
  /** Icon color from the DTO; otherwise the tone's color, otherwise muted. */
  iconColor?: string;
  tone?: Tone;
  /** Makes the title a link. */
  href?: string;
  /** A 3 px leading border in this color (call priority). */
  accentColor?: string;
  /** Briefly highlights the row (a new call arriving). */
  isNew?: boolean;
}

export function ItemList({ items, className }: { items: ListItem[]; className?: string }) {
  return (
    <ul className={`rg-list${className ? ` ${className}` : ''}`}>
      {items.map((item, index) => {
        const iconClass = item.icon ?? (item.tone ? TONE_ICON[item.tone] : undefined);
        const style = item.accentColor ? ({ '--pri': item.accentColor } as CSSProperties) : undefined;
        const classes = [item.accentColor ? 'rg-call' : '', item.isNew ? 'rg-row-new' : ''].filter(Boolean).join(' ');
        return (
          <li key={item.key ?? index} className={classes || undefined} style={style}>
            {iconClass ? (
              <i
                className={`fa ${iconClass} rg-list-icon${item.tone && !item.iconColor ? ` rg-tone-${item.tone}` : ''}`}
                style={item.iconColor ? { color: item.iconColor } : undefined}
                aria-hidden="true"
              />
            ) : null}
            <div className="rg-list-grow">
              <div className="rg-t">{item.href ? <a href={item.href}>{item.title}</a> : item.title}</div>
              {item.meta ? <div className="rg-m">{item.meta}</div> : null}
            </div>
            {item.right !== undefined && item.right !== null ? <div className="rg-r">{item.right}</div> : null}
          </li>
        );
      })}
    </ul>
  );
}

export interface AgendaItem {
  key?: string;
  /** Short day label ("Today", "Sun 1"), already in the department's zone. */
  day: string;
  /** Time label ("19:00"). */
  time: string;
  title: ReactNode;
  meta?: ReactNode;
  right?: ReactNode;
  href?: string;
}

export function Agenda({ items }: { items: AgendaItem[] }) {
  return (
    <ul className="rg-list rg-agenda">
      {items.map((item, index) => (
        <li key={item.key ?? index}>
          <div className="rg-when">
            <span className="rg-d">{item.day}</span>
            <span className="rg-h">{item.time}</span>
          </div>
          <div className="rg-list-grow">
            <div className="rg-t">{item.href ? <a href={item.href}>{item.title}</a> : item.title}</div>
            {item.meta ? <div className="rg-m">{item.meta}</div> : null}
          </div>
          {item.right !== undefined && item.right !== null ? <div className="rg-r">{item.right}</div> : null}
        </li>
      ))}
    </ul>
  );
}

export interface TableColumn<TRow> {
  key: string;
  header: string;
  render?: (row: TRow) => ReactNode;
  /** Muted text color for secondary columns. */
  muted?: boolean;
  align?: 'start' | 'end';
  /** Truncate long text (notes) at this width. */
  maxWidth?: number;
}

export interface TableGroup<TRow> {
  key: string;
  label: ReactNode;
  /** Counts after the label ("8 personnel · 5 committed"). */
  meta?: ReactNode;
  rows: TRow[];
}

export interface DenseTableProps<TRow> {
  columns: TableColumn<TRow>[];
  rows?: TRow[];
  /** Grouped rows with collapsible group headers (the personnel board). Use instead of rows. */
  groups?: TableGroup<TRow>[];
  rowKey: (row: TRow) => string;
  collapsedGroups?: readonly string[];
  onToggleGroup?: (groupKey: string) => void;
  highlightRowKeys?: readonly string[];
  emptyText?: string;
  caption?: string;
}

export function DenseTable<TRow>(props: DenseTableProps<TRow>) {
  const highlight = new Set(props.highlightRowKeys ?? []);
  const collapsed = new Set(props.collapsedGroups ?? []);
  const cell = (row: TRow, column: TableColumn<TRow>) => {
    const value = column.render ? column.render(row) : ((row as Record<string, unknown>)[column.key] as ReactNode);
    return (
      <td
        key={column.key}
        className={column.muted ? 'rg-mu' : undefined}
        style={{ textAlign: column.align === 'end' ? 'end' : undefined, maxWidth: column.maxWidth }}
      >
        {value}
      </td>
    );
  };
  const renderRows = (rows: TRow[]) =>
    rows.map((row) => {
      const key = props.rowKey(row);
      return (
        <tr key={key} className={highlight.has(key) ? 'rg-row-new' : undefined}>
          {props.columns.map((column) => cell(row, column))}
        </tr>
      );
    });

  const empty = (props.rows?.length ?? 0) === 0 && (props.groups ?? []).every((group) => group.rows.length === 0);

  return (
    <table className="rg-table">
      {props.caption ? <caption className="sr-only">{props.caption}</caption> : null}
      <thead>
        <tr>
          {props.columns.map((column) => (
            <th key={column.key} scope="col" style={{ textAlign: column.align === 'end' ? 'end' : undefined }}>
              {column.header}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {props.groups
          ? props.groups.map((group) => {
              const isCollapsed = collapsed.has(group.key);
              return (
                <Fragment key={group.key}>
                  <tr className="rg-grp">
                    <td colSpan={props.columns.length}>
                      <button
                        type="button"
                        className="rg-grp-toggle"
                        aria-expanded={!isCollapsed}
                        onClick={() => props.onToggleGroup?.(group.key)}
                        disabled={!props.onToggleGroup}
                      >
                        <i className={`fa fa-caret-${isCollapsed ? 'right' : 'down'}`} aria-hidden="true" /> {group.label}
                        {group.meta ? <small>{group.meta}</small> : null}
                      </button>
                    </td>
                  </tr>
                  {isCollapsed ? null : renderRows(group.rows)}
                </Fragment>
              );
            })
          : renderRows(props.rows ?? [])}
        {empty && props.emptyText ? (
          <tr>
            <td colSpan={props.columns.length} className="rg-mu rg-table-empty">
              {props.emptyText}
            </td>
          </tr>
        ) : null}
      </tbody>
    </table>
  );
}

/**
 * Renders a DashboardProtectedText (plan §6.4): plain text when not protected; otherwise a shield and a muted
 * placeholder — a button that opens the step-up when the viewer can reveal, a plain "Your role can't view this" marker
 * when not. Never shows the server's REDACTED string or a blank.
 */
export function ProtectedText({ value, placeholder }: { value: DashboardProtectedText | string | null | undefined; placeholder?: string }) {
  const { adp, t } = usePrimitiveRuntime();
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === 'string') {
    return <>{value}</>;
  }
  if (!value.IsProtected) {
    return value.Value ? <span className={adp.revealed ? 'rg-revealed-val' : undefined}>{value.Value}</span> : null;
  }

  const label = placeholder || t('Dash.Protected');
  if (!value.CanReveal) {
    return (
      <span className="rg-prot rg-prot-denied" title={t('Dash.AdpRoleCannotView')}>
        <i className="fa fa-shield" aria-hidden="true" /> {label}
      </span>
    );
  }
  if (!adp.canReveal) {
    return (
      <span className="rg-prot" title={t('Dash.AdpVerifyTooltip')}>
        <i className="fa fa-shield" aria-hidden="true" /> {label}
      </span>
    );
  }
  return (
    <button type="button" className="rg-prot" title={t('Dash.AdpVerifyTooltip')} aria-label={t('Dash.AdpProtectedAria', label)} onClick={adp.requestReveal}>
      <i className="fa fa-shield" aria-hidden="true" /> {label}
    </button>
  );
}
