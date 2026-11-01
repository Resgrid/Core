import { BarList, EmptyState, HeroNumber, KpiGrid, Note } from '../../primitives';
import type { Tone } from '../../primitives';
import type { WidgetProps } from '../types';
import { amounts, toneIf, type CurrencyAmount } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/InvoicingReceivablesWidgetData.cs */
export interface InvoicingReceivablesWidgetData {
  ReadOnly: boolean;
  Buckets: InvoicingAgingBucket[];
  Outstanding: CurrencyAmount[];
  Overdue: CurrencyAmount[];
  OpenCount: number;
  OverdueCount: number;
  DraftCount: number;
}

export interface InvoicingAgingBucket {
  /** "Current", "1-30", "31-60", "61-90", "90+". */
  Key: string;
  Count: number;
  Balances: CurrencyAmount[];
}

const AGING_URL = '/User/Invoicing/Aging';
const LIST_URL = '/User/Invoicing';

function bucketTone(key: string, count: number): Tone | undefined {
  if (!count) return undefined;
  if (key === '90+') return 'bad';
  if (key === '61-90') return 'warn';
  return undefined;
}

/** invoicing.receivables (plan §7.9): aging buckets and balances per currency; read-only when the add-on lapsed. */
export default function InvoicingReceivablesWidget({ data, size, t, ctx }: WidgetProps<InvoicingReceivablesWidgetData>) {
  const k = 'W.invoicing.receivables.';
  if (!data.OpenCount && !data.DraftCount) {
    return <EmptyState icon="fa-file-text-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenInvoicing`)} href={LIST_URL} />;
  }

  const outstanding = data.Outstanding ?? [];
  const single = outstanding.length <= 1;
  const currency = outstanding[0]?.Currency || 'USD';
  const balanceOf = (bucket: InvoicingAgingBucket) => bucket.Balances?.find((b) => (b.Currency || 'USD') === currency)?.Amount ?? 0;
  const buckets = data.Buckets ?? [];

  return (
    <>
      {size === 'm' ? (
        <div className="rg-m" style={{ marginBottom: 6 }}>
          <b style={{ color: 'var(--rg-heading)', fontSize: 18 }}>{single ? amounts(outstanding, ctx.format) : ctx.format.number(data.OpenCount, 0)}</b>{' '}
          <span className="rg-mu">{single ? t(`${k}OutstandingCount`, data.OpenCount) : t(`${k}OpenInvoices`)}</span>
        </div>
      ) : (
        <HeroNumber
          size="md"
          value={single ? amounts(outstanding, ctx.format) : ctx.format.number(data.OpenCount, 0)}
          label={single ? t(`${k}OutstandingCount`, data.OpenCount) : t(`${k}OpenInvoices`)}
        />
      )}
      {!single ? <Note>{amounts(outstanding, ctx.format)}</Note> : null}
      {size === 'm' || size === 'l' || size === 'xl' || size === 'full' ? (
        <BarList
          items={buckets.map((bucket) => ({
            label: t(`${k}Bucket.${bucket.Key}`),
            // One currency: bars by balance. Several: bars by invoice count, each currency's balance in the label.
            value: single ? balanceOf(bucket) : bucket.Count,
            display: single ? ctx.format.currency(balanceOf(bucket), currency) : `${ctx.format.number(bucket.Count, 0)} · ${amounts(bucket.Balances, ctx.format)}`,
            tone: bucketTone(bucket.Key, bucket.Count),
            href: AGING_URL,
          }))}
        />
      ) : null}
      {size === 'm' ? (
        <Note>
          {data.OverdueCount ? <i className="fa fa-exclamation-triangle" aria-hidden="true" /> : null} {t(`${k}OverdueSummary`, data.OverdueCount, amounts(data.Overdue, ctx.format))}
          {data.DraftCount ? ` · ${t(`${k}DraftsCount`, data.DraftCount)}` : ''}
          {data.ReadOnly ? ` · ${t(`${k}ReadOnlyShort`)}` : ''}
        </Note>
      ) : (
      <KpiGrid
        columns={2}
        items={[
          { value: ctx.format.number(data.OverdueCount, 0), label: t(`${k}OverdueCount`), tone: toneIf(data.OverdueCount, 'warn'), href: AGING_URL, title: amounts(data.Overdue, ctx.format) },
          { value: ctx.format.number(data.DraftCount, 0), label: t(`${k}Drafts`), href: LIST_URL },
        ]}
      />
      )}
      {data.ReadOnly && size !== 'm' ? <Note>{t(`${k}ReadOnly`)}</Note> : null}
    </>
  );
}
