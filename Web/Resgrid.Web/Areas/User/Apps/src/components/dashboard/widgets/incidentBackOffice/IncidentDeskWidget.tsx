import { BarList, EmptyState, KpiGrid, SubHeading } from '../../primitives';
import type { Tone } from '../../primitives';
import type { WidgetProps } from '../types';
import { ActivationHeader, deskUrl, type IncidentArrivalWindow, type IncidentWidgetDataBase, NoActivation, ReadOnlyNote, compactCurrency, wholeCurrency } from './iboShared';
import './ibo.css';

/** Core/Resgrid.Model/Dashboard/Widgets/IncidentBackOffice/IncidentDeskWidgetData.cs */
export interface IncidentDeskWidgetData extends IncidentWidgetDataBase {
  /** "operations", "logistics" or "finance". */
  Desk: string;
  AsOf?: string | null;
  Values: Record<string, number>;
  Inbound: IncidentArrivalWindow[];
  Outbound: IncidentArrivalWindow[];
  DeskUnavailable: boolean;
  ProjectedEndOn?: string | null;
}

const DESK_PATHS: Record<string, string> = {
  operations: '/User/IncidentOperations',
  logistics: '/User/IncidentLogistics',
  finance: '/User/IncidentFinance',
};

// Values in the order each desk reads them; anything else the server adds is shown after these.
const ORDER: Record<string, string[]> = {
  operations: [
    'participants.on_incident', 'participants.inbound', 'resources.on_incident', 'resources.inbound', 'requests.open',
    'requests.critical_unfilled', 'requests.overdue', 'participants.total', 'resources.personnel',
  ],
  logistics: ['fills.en_route', 'demob.pending', 'lodging.needed', 'lodging.beds_available_tonight', 'lodging.beds_held_tonight', 'service.open'],
  finance: [
    'finance.cost_to_date', 'finance.daily_burn', 'finance.committed', 'finance.budget', 'finance.forecast_at_completion', 'finance.open_commitments',
    'finance.time_awaiting_certification', 'finance.time_awaiting_approval', 'finance.invoice_exceptions',
  ],
};

const MONEY = new Set(['finance.cost_to_date', 'finance.daily_burn', 'finance.committed', 'finance.budget', 'finance.forecast_at_completion']);
const WARN = new Set(['requests.critical_unfilled', 'requests.overdue', 'lodging.needed', 'finance.invoice_exceptions']);

/** ibo.desk (plan §7.10, multiple instances): one incident's operations, logistics or finance desk at a glance. */
export default function IncidentDeskWidget({ data, size, t, ctx }: WidgetProps<IncidentDeskWidgetData>) {
  const k = 'W.ibo.desk.';
  if (!data.HasActivation) {
    return <NoActivation t={t} />;
  }

  const desk = DESK_PATHS[data.Desk] ? data.Desk : 'operations';
  const href = deskUrl(DESK_PATHS[desk], data.ActivationId);
  if (data.DeskUnavailable) {
    return (
      <>
        <ActivationHeader data={data} t={t} href={href} />
        <EmptyState icon="fa-lock" text={t(`${k}DeskUnavailable`)} />
      </>
    );
  }

  const values = data.Values ?? {};
  const keys = [...ORDER[desk].filter((key) => key in values), ...Object.keys(values).filter((key) => !ORDER[desk].includes(key))];
  const limit = size === 'm' ? 4 : size === 'l' ? 6 : keys.length;
  const currency = data.Currency || 'USD';
  const windows = (list: IncidentArrivalWindow[] | null | undefined, outbound: boolean) =>
    (list ?? []).filter((window) => window.Count || window.Predicted).map((window) => ({
      label: window.Window,
      value: outbound ? window.Count + window.Predicted : window.Count,
      display: outbound
        ? `${ctx.format.number(window.Count, 0)}${window.Predicted ? ` · ${t(`${k}Predicted`)} ${ctx.format.number(window.Predicted, 0)}` : ''}`
        : `${ctx.format.number(window.Count, 0)}${window.Personnel ? ` · ${t(`${k}PersonnelShort`, window.Personnel)}` : ''}`,
    }));
  const showWindows = size !== 'm';

  return (
    <>
      <ActivationHeader data={data} t={t} href={href} />
      {keys.length === 0 ? (
        <EmptyState icon="fa-th-large" text={t(`${k}NoValues`)} />
      ) : (
        <KpiGrid
          columns={size === 'm' ? 2 : size === 'l' ? 3 : undefined}
          items={keys.slice(0, limit).map((key) => ({
            value: MONEY.has(key) ? compactCurrency(values[key], currency) : ctx.format.number(values[key], 0),
            title: MONEY.has(key) ? wholeCurrency(values[key], currency) : undefined,
            label: t(`${k}Value.${key}`),
            tone: WARN.has(key) && values[key] ? ('warn' as Tone) : undefined,
            href,
          }))}
        />
      )}
      {showWindows && desk !== 'finance' && (data.Inbound ?? []).length > 0 ? (
        <>
          <SubHeading>{t(`${k}Inbound`)}</SubHeading>
          <BarList items={windows(data.Inbound, false)} />
        </>
      ) : null}
      {showWindows && desk === 'logistics' && (data.Outbound ?? []).length > 0 ? (
        <>
          <SubHeading>{t(`${k}Outbound`)}</SubHeading>
          <BarList items={windows(data.Outbound, true)} />
        </>
      ) : null}
      <ReadOnlyNote readOnly={data.ReadOnly} t={t} />
    </>
  );
}
