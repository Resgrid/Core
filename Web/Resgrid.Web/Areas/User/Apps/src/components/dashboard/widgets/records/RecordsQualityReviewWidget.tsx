import { EmptyState, ItemList, KpiGrid, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { enc, joinMeta, rowsFor } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsQualityReviewWidgetData.cs */
export interface RecordsQualityReviewItem {
  ReviewId: string;
  RecordId?: string | null;
  RecordNumber?: string | null;
  DefinitionKey?: string | null;
  SampledOn: string;
  AssignedToMe: boolean;
}

export interface RecordsQualityReviewWidgetData {
  Pending: number;
  AssignedToMe: number;
  HasMore: boolean;
  Items: RecordsQualityReviewItem[];
}

const QUALITY_URL = '/User/RecordsQuality';

/** records.qualityReview (plan §7.8): records waiting for quality review, the viewer's first. */
export default function RecordsQualityReviewWidget({ data, size, t, ctx }: WidgetProps<RecordsQualityReviewWidgetData>) {
  const k = 'W.records.qualityReview.';
  useWidgetBadge(ctx, data.AssignedToMe > 0 ? data.AssignedToMe : null);

  if (!data.Pending) {
    return <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`)} linkText={t(`${k}OpenQuality`)} href={QUALITY_URL} />;
  }

  return (
    <>
      <KpiGrid
        columns={2}
        items={[
          { value: data.HasMore ? `${ctx.format.number(data.Pending, 0)}+` : ctx.format.number(data.Pending, 0), label: t(`${k}Pending`), href: QUALITY_URL },
          { value: ctx.format.number(data.AssignedToMe, 0), label: t(`${k}AssignedToMe`), href: QUALITY_URL },
        ]}
      />
      <ItemList
        items={rowsFor(data.Items, size).map((review) => ({
          key: review.ReviewId,
          title: review.RecordNumber || t(`${k}NoNumber`),
          href: `/User/RecordsQuality/Review?id=${enc(review.ReviewId)}`,
          meta: joinMeta(t(`${k}Sampled`, ctx.format.ago(review.SampledOn)), review.AssignedToMe ? t(`${k}Yours`) : null),
          icon: review.AssignedToMe ? 'fa-user' : 'fa-check-circle',
        }))}
      />
      {data.Items.length > rowsFor(data.Items, size).length || data.HasMore ? (
        <Note>
          <a href={QUALITY_URL}>{t(`${k}SeeAll`)}</a>
        </Note>
      ) : null}
    </>
  );
}
