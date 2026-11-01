import { EmptyState, HeroNumber, ItemList, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, isCompact, joinMeta, rowsFor } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsLegalHoldsWidgetData.cs */
export interface RecordsLegalHoldItem {
  HoldId: string;
  RecordId?: string | null;
  DefinitionKey?: string | null;
  PeriodStart?: string | null;
  PeriodEnd?: string | null;
  PlacedOn: string;
}

export interface RecordsLegalHoldsWidgetData {
  Active: number;
  OnRecords: number;
  NewestPlacedOn?: string | null;
  Holds: RecordsLegalHoldItem[];
}

const HOLDS_URL = '/User/RecordLegalHolds';

/** records.legalHolds (plan §7.8): active preservation holds (reference numbers and notes stay on the module page). */
export default function RecordsLegalHoldsWidget({ data, size, t, ctx }: WidgetProps<RecordsLegalHoldsWidgetData>) {
  const k = 'W.records.legalHolds.';
  useWidgetBadge(ctx, data.Active > 0 ? data.Active : null);

  if (!data.Active) {
    return <EmptyState icon="fa-lock" text={t(`${k}Empty`)} linkText={t(`${k}OpenHolds`)} href={HOLDS_URL} />;
  }

  const period = (hold: RecordsLegalHoldItem) =>
    hold.PeriodStart || hold.PeriodEnd ? t(`${k}Period`, ctx.format.date(hold.PeriodStart), ctx.format.date(hold.PeriodEnd)) : null;

  return (
    <>
      <HeroNumber value={ctx.format.number(data.Active, 0)} label={t(`${k}ActiveHolds`)} icon="fa-lock" size="md" />
      {isCompact(size) ? (
        <Note>
          {joinMeta(t(`${k}OnRecords`, data.OnRecords), data.NewestPlacedOn ? t(`${k}Newest`, ctx.format.date(data.NewestPlacedOn)) : null)}
        </Note>
      ) : (
        <ItemList
          items={rowsFor(data.Holds, size).map((hold) => ({
            key: hold.HoldId,
            title: hold.RecordId ? t(`${k}RecordHold`) : t(`${k}ScopeHold`),
            href: hold.RecordId ? `${HOLDS_URL}?recordId=${enc(hold.RecordId)}` : HOLDS_URL,
            meta: joinMeta(hold.DefinitionKey, period(hold)),
            right: <span className="rg-mu">{t(`${k}Placed`, ctx.format.date(hold.PlacedOn))}</span>,
            icon: 'fa-lock',
          }))}
        />
      )}
    </>
  );
}
