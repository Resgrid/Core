import { EmptyState, ItemList, KpiGrid, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Intelligence/AiDispatchTriageWidgetData.cs */
export interface AiDispatchTriageWidgetData {
  Hours: number;
  NeedsReview: number;
  InProgress: number;
  Applied: number;
  Total: number;
  Rows: AiDispatchTriageRow[];
}

export interface AiDispatchTriageRow {
  CallId: number;
  CallNumber?: string | null;
  /** AiDispatchOutcomes value. */
  Outcome: string;
  ConfidencePercent?: number | null;
  RelatedCallId?: number | null;
  RelatedCallNumber?: string | null;
  CreatedOnUtc: string;
  NeedsReview: boolean;
}

/** aiDispatch.triage (plan §7.1): calls created from inbound email/SMS that still need a dispatcher's look. */
export default function AiDispatchTriageWidget({ data, size, t, ctx }: WidgetProps<AiDispatchTriageWidgetData>) {
  const k = 'W.aiDispatch.triage.';
  useWidgetBadge(ctx, data.NeedsReview || null);

  const rows = data.Rows ?? [];
  return (
    <>
      <KpiGrid
        columns={3}
        items={[
          { value: ctx.format.number(data.NeedsReview, 0), label: t(`${k}NeedsReview`), tone: data.NeedsReview ? 'warn' : undefined },
          { value: ctx.format.number(data.InProgress, 0), label: t(`${k}InProgress`) },
          { value: ctx.format.number(data.Applied, 0), label: t(`${k}Applied`) },
        ]}
      />
      {rows.length === 0 ? (
        <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`)} />
      ) : (
        <ItemList
          items={rows.slice(0, size === 'm' ? 2 : undefined).map((row) => ({
            key: String(row.CallId),
            title: row.CallNumber || `#${row.CallId}`,
            meta: [
              t(`${k}Outcome.${row.Outcome}`),
              row.ConfidencePercent !== null && row.ConfidencePercent !== undefined ? t(`${k}Confidence`, row.ConfidencePercent) : null,
              row.RelatedCallNumber ? t(`${k}Related`, row.RelatedCallNumber) : null,
            ]
              .filter(Boolean)
              .join(' · '),
            tone: row.NeedsReview ? 'warn' : 'good',
            right: <span className="rg-mu">{ctx.format.ago(row.CreatedOnUtc)}</span>,
            href: `/User/Dispatch/ViewCall?callId=${encodeURIComponent(String(row.CallId))}`,
          }))}
        />
      )}
      {size !== 'm' ? <Note>{t(`${k}Window`, data.Hours, data.Total)}</Note> : null}
    </>
  );
}
