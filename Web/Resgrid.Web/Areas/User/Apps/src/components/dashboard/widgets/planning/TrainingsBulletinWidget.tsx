import { EmptyState, ItemList, type ListItem } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/TrainingsBulletinWidgetData.cs */
export interface TrainingsBulletinWidgetData {
  NewAssignedCount: number;
  CatalogCount: number;
  Items: Array<{
    TrainingId: number;
    Name?: string | null;
    Kind: 'assigned' | 'catalog' | string;
    PublishedUtc: string;
    DueUtc?: string | null;
    IsOverdue: boolean;
    CanSelfEnroll: boolean;
    Url: string;
  }>;
}

/** trainings.bulletin (Training T0 item 6): what is new in training for the member — new assignments and newly published open trainings. */
export default function TrainingsBulletinWidget({ data, t, ctx }: WidgetProps<TrainingsBulletinWidgetData>) {
  useWidgetBadge(ctx, data.NewAssignedCount > 0 ? data.NewAssignedCount : null);

  if (data.Items.length === 0) {
    return <EmptyState icon="fa-bullhorn" text={t('W.trainings.bulletin.Empty')} />;
  }

  return (
    <>
      {data.NewAssignedCount > 0 ? (
        <div style={{ fontSize: 12, marginBottom: 4 }}>
          <i className="fa fa-book" aria-hidden="true" /> {t('W.trainings.bulletin.NewAssignedCount', data.NewAssignedCount)}
        </div>
      ) : null}
      <ItemList
        items={data.Items.map((training): ListItem => {
          const assigned = training.Kind === 'assigned';
          return {
            key: `${training.Kind}-${training.TrainingId}`,
            icon: assigned ? 'fa-book' : 'fa-bookmark-o',
            tone: training.IsOverdue ? 'bad' : assigned ? 'info' : undefined,
            title: training.Name || t('W.trainings.bulletin.Training'),
            href: training.Url,
            meta: [
              assigned ? t('W.trainings.bulletin.Assigned') : training.CanSelfEnroll ? t('W.trainings.bulletin.CanEnrol') : t('W.trainings.bulletin.Catalog'),
              training.DueUtc
                ? training.IsOverdue
                  ? t('W.trainings.bulletin.OverdueSince', ctx.format.date(training.DueUtc))
                  : t('W.trainings.bulletin.Due', ctx.format.date(training.DueUtc))
                : t('W.trainings.bulletin.Published', ctx.format.date(training.PublishedUtc)),
            ]
              .filter(Boolean)
              .join(' · '),
          };
        })}
      />
    </>
  );
}
