import { BarList, EmptyState, HeroNumber, Note, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import { isCompact } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/InventoryValuationWidgetData.cs */
export interface InventoryValuationTotalItem {
  CurrencyCode?: string | null;
  KnownValue: number;
  UncostedRows: number;
}

export interface InventoryValuationLocationItem {
  LocationId: string;
  Name?: string | null;
  CurrencyCode?: string | null;
  KnownValue: number;
  Lines: number;
}

export interface InventoryValuationWidgetData {
  Migrated: boolean;
  AsOfUtc?: string | null;
  HasNegativeStock: boolean;
  Totals: InventoryValuationTotalItem[];
  Locations: InventoryValuationLocationItem[];
}

const VALUATION_URL = '/User/Inventory/Purchasing?tab=Valuation';

/** inventory.valuation (plan §7.7): value on hand per currency and the holders carrying the most. */
export default function InventoryValuationWidget({ data, size, t, ctx }: WidgetProps<InventoryValuationWidgetData>) {
  const k = 'W.inventory.valuation.';
  if (!data.Migrated) {
    return <EmptyState icon="fa-barcode" text={t(`${k}NotInitialized`)} linkText={t(`${k}OpenInventory`)} href="/User/Inventory" />;
  }

  const totals = data.Totals ?? [];
  if (!totals.length) {
    return <EmptyState icon="fa-barcode" text={t(`${k}Empty`)} linkText={t(`${k}OpenValuation`)} href={VALUATION_URL} />;
  }

  const money = (value: number, currency?: string | null) => (currency ? ctx.format.currency(value, currency) : ctx.format.number(value, 2));
  const [first, ...others] = totals;
  const uncosted = totals.reduce((sum, total) => sum + (total.UncostedRows ?? 0), 0);

  return (
    <>
      <HeroNumber value={money(first.KnownValue, first.CurrencyCode)} label={t(`${k}OnHand`)} size="md" />
      {others.length ? <Note>{others.map((total) => money(total.KnownValue, total.CurrencyCode)).join(' · ')}</Note> : null}
      {!isCompact(size) && data.Locations?.length ? (
        <>
          <SubHeading>{t(`${k}ByLocation`)}</SubHeading>
          <BarList
            items={data.Locations.map((location) => ({
              label: location.Name || t(`${k}UnnamedLocation`),
              value: location.KnownValue,
              display: money(location.KnownValue, location.CurrencyCode),
            }))}
          />
        </>
      ) : null}
      {data.HasNegativeStock ? (
        <Note>
          <i className="fa fa-exclamation-triangle rg-list-icon rg-tone-warn" aria-hidden="true" /> {t(`${k}NegativeStock`)}
        </Note>
      ) : null}
      {uncosted > 0 ? <Note>{t(`${k}Uncosted`, uncosted)}</Note> : null}
      {data.AsOfUtc && !isCompact(size) ? <Note>{t(`${k}AsOf`, ctx.format.dateTime(data.AsOfUtc))}</Note> : null}
    </>
  );
}
