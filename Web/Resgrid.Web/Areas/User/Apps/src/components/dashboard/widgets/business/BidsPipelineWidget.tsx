import { EmptyState, KpiGrid, Note } from '../../primitives';
import type { KpiItem } from '../../primitives';
import type { WidgetProps } from '../types';
import { isCompact, toneIf } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/BidsPipelineWidgetData.cs */
export interface BidsPipelineWidgetData {
  Draft: number;
  Submitted: number;
  Accepted: number;
  Declined: number;
  Expired: number;
  Withdrawn: number;
  ExpiringSoon: number;
  ExpiringDays: number;
}

const LIST_URL = '/User/Bids';
// BidStatuses values for the list filter.
const STATUS = { Draft: 0, Submitted: 1, Accepted: 2, Declined: 3, Expired: 4, Withdrawn: 5 } as const;

/** bids.pipeline (plan §7.9): bids by status, and sent bids about to lapse. */
export default function BidsPipelineWidget({ data, size, t, ctx }: WidgetProps<BidsPipelineWidgetData>) {
  const k = 'W.bids.pipeline.';
  const total = data.Draft + data.Submitted + data.Accepted + data.Declined + data.Expired + data.Withdrawn;
  if (!total) {
    return <EmptyState icon="fa-handshake-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenBids`)} href={LIST_URL} />;
  }

  const kpi = (name: keyof typeof STATUS, value: number): KpiItem => ({
    value: ctx.format.number(value, 0),
    label: t(`${k}${name}`),
    href: `${LIST_URL}?status=${STATUS[name]}`,
  });
  const expiring: KpiItem = {
    value: ctx.format.number(data.ExpiringSoon, 0),
    label: t(`${k}ExpiringWithin`, ctx.format.duration(data.ExpiringDays * 1440)),
    tone: toneIf(data.ExpiringSoon, 'warn'),
    href: `${LIST_URL}?status=${STATUS.Submitted}`,
  };
  // s: two by two; m: three by two. The expiring tile is always last so the pipeline reads left to right.
  const items = isCompact(size)
    ? [kpi('Submitted', data.Submitted), kpi('Accepted', data.Accepted), kpi('Draft', data.Draft), expiring]
    : [kpi('Draft', data.Draft), kpi('Submitted', data.Submitted), kpi('Accepted', data.Accepted), kpi('Declined', data.Declined), kpi('Expired', data.Expired), expiring];

  return (
    <>
      <KpiGrid columns={isCompact(size) ? 2 : 3} items={items} />
      {!isCompact(size) && data.Withdrawn ? <Note>{t(`${k}WithdrawnCount`, data.Withdrawn)}</Note> : null}
    </>
  );
}
