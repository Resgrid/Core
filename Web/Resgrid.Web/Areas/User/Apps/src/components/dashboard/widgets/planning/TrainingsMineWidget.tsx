import { EmptyState, ItemList } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/TrainingsMineWidgetData.cs */
export interface TrainingsMineWidgetData {
  OutstandingCount: number;
  OverdueCount: number;
  Items: Array<{
    TrainingId: number;
    Name?: string | null;
    DueUtc?: string | null;
    IsOverdue: boolean;
    Viewed: boolean;
    QuizFailed: boolean;
    Url: string;
  }>;
}

/** trainings.mine (plan §7.6): assigned trainings the member has not completed, soonest due first. */
export default function TrainingsMineWidget({ data, t, ctx }: WidgetProps<TrainingsMineWidgetData>) {
  useWidgetBadge(ctx, data.OutstandingCount > 0 ? data.OutstandingCount : null);

  if (data.Items.length === 0) {
    return <EmptyState icon="fa-check" text={t('W.trainings.mine.Empty')} />;
  }

  return (
    <>
      {data.OverdueCount > 0 ? (
        <div style={{ color: 'var(--rg-danger-text)', fontSize: 12, marginBottom: 4 }}>
          <i className="fa fa-exclamation-circle" aria-hidden="true" /> {t('W.trainings.mine.OverdueCount', data.OverdueCount)}
        </div>
      ) : null}
      <ItemList
        items={data.Items.map((training) => ({
          key: String(training.TrainingId),
          icon: 'fa-book',
          tone: training.IsOverdue ? 'bad' : training.QuizFailed ? 'warn' : undefined,
          title: training.Name || t('W.trainings.mine.Training'),
          href: training.Url,
          meta: [
            training.QuizFailed ? t('W.trainings.mine.QuizFailed') : training.Viewed ? t('W.trainings.mine.InProgress') : t('W.trainings.mine.NotStarted'),
            training.DueUtc ? (training.IsOverdue ? t('W.trainings.mine.OverdueSince', ctx.format.date(training.DueUtc)) : t('W.trainings.mine.Due', ctx.format.date(training.DueUtc))) : null,
          ]
            .filter(Boolean)
            .join(' · '),
        }))}
      />
    </>
  );
}
