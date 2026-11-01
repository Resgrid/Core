import { BarList, EmptyState, ItemList, KpiGrid, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { isCompact, joinMeta, rowsFor, toneIf, toTone } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/InventoryAlertsWidgetData.cs */
export interface InventoryAlertsItem {
  /** InventoryAlertType: 0 low stock, 1 expiring soon, 2 expired, 3 overdue return, 4 departed holder. */
  AlertType: number;
  ItemId?: string | null;
  ItemName?: string | null;
  LocationId?: string | null;
  Count: number;
  Quantity?: number | null;
  DueOn?: string | null;
  OpenedOn?: string | null;
  Tone?: string | null;
}

export interface InventoryAlertsWidgetData {
  Migrated: boolean;
  LowStock: number;
  ExpiringSoon: number;
  Expired: number;
  OverdueReturn: number;
  DepartedHolder: number;
  Total: number;
  Truncated: boolean;
  Items: InventoryAlertsItem[];
}

const ALERTS_URL = '/User/Inventory/Operations?tab=Alerts';
const INVENTORY_URL = '/User/Inventory';

/** inventory.alerts (plan §7.7): open alerts by type and the most urgent items (legacy low stock before initialization). */
export default function InventoryAlertsWidget({ data, size, t, ctx }: WidgetProps<InventoryAlertsWidgetData>) {
  const k = 'W.inventory.alerts.';
  useWidgetBadge(ctx, data.Total > 0 ? data.Total : null);
  const compact = isCompact(size);

  const itemRows = (items: InventoryAlertsItem[]) =>
    items.map((item, index) => ({
      key: `${item.AlertType}-${item.ItemId ?? index}-${item.LocationId ?? ''}`,
      title: item.ItemName || t(`${k}UnknownItem`),
      meta: joinMeta(
        t(`${k}Type${item.AlertType}`),
        item.DueOn ? (item.AlertType === 1 ? t(`${k}ExpiresOn`, ctx.format.date(item.DueOn)) : t(`${k}DueOn`, ctx.format.date(item.DueOn))) : null,
        item.Quantity !== null && item.Quantity !== undefined ? t(`${k}Quantity`, ctx.format.number(item.Quantity, 2)) : null,
      ),
      tone: toTone(item.Tone),
      right: item.Count > 1 ? <b className="rg-num">{t(`${k}TimesCount`, item.Count)}</b> : null,
    }));

  if (!data.Migrated) {
    if (!data.LowStock) {
      return <EmptyState icon="fa-barcode" text={t(`${k}LegacyEmpty`)} linkText={t(`${k}OpenInventory`)} href={INVENTORY_URL} />;
    }
    return (
      <>
        <KpiGrid items={[{ value: ctx.format.number(data.LowStock, 0), label: t(`${k}OutOfStock`), tone: 'warn', href: INVENTORY_URL }]} />
        {!compact ? <ItemList items={itemRows(rowsFor(data.Items, size))} /> : null}
        <Note>
          <a href={INVENTORY_URL}>{t(`${k}LegacyNote`)}</a>
        </Note>
      </>
    );
  }

  if (!data.Total) {
    return <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenAlerts`)} href={ALERTS_URL} />;
  }

  const kinds = [
    { type: 2, value: data.Expired, tone: toneIf(data.Expired, 'bad') },
    { type: 3, value: data.OverdueReturn, tone: toneIf(data.OverdueReturn, 'bad') },
    { type: 4, value: data.DepartedHolder, tone: toneIf(data.DepartedHolder, 'bad') },
    { type: 0, value: data.LowStock, tone: toneIf(data.LowStock, 'warn') },
    { type: 1, value: data.ExpiringSoon, tone: undefined },
  ];

  return (
    <>
      {compact ? (
        <KpiGrid
          columns={2}
          items={kinds
            .filter((kind) => kind.value > 0)
            .slice(0, 4)
            .map((kind) => ({ value: ctx.format.number(kind.value, 0), label: t(`${k}Type${kind.type}`), tone: kind.tone, href: ALERTS_URL }))}
        />
      ) : (
        <BarList items={kinds.filter((kind) => kind.value > 0).map((kind) => ({ label: t(`${k}Type${kind.type}`), value: kind.value, tone: kind.tone, href: ALERTS_URL }))} />
      )}
      {!compact && data.Items?.length ? <ItemList items={itemRows(size === 'm' ? data.Items.slice(0, 2) : data.Items)} /> : null}
      {data.Truncated ? <Note>{t(`${k}Truncated`)}</Note> : null}
    </>
  );
}
