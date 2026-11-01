import { BarList, HeroNumber, ItemList, Note } from '../../primitives';
import type { ListItem } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Administration/AdminSetupWidgetData.cs */
export interface AdminSetupWidgetData {
  SetupMode: boolean;
  ChecksRequired: number;
  ChecksVerified: number;
  ChecksFailed: number;
  ChecksUnknown: number;
  PercentComplete: number;
  TasksTotal: number;
  TasksDone: number;
  NeedsAttention: number;
  /** The next pending task's capability, already localized. */
  NextLabel?: string | null;
  NextUrl: string;
  RevisitDue: boolean;
  RevisitOnUtc?: string | null;
}

const WIZARD_URL = '/User/AdminAssist/SetupWizard';
const REPORT_URL = '/User/AdminAssist/SetupReport';

/** admin.setup (plan §7.11): Admin Assist setup progress — checks verified, tasks done and the next one to do. */
export default function AdminSetupWidget({ data, size, t, ctx }: WidgetProps<AdminSetupWidgetData>) {
  const k = 'W.admin.setup.';
  const done = data.TasksTotal > 0 && data.TasksDone >= data.TasksTotal && !data.ChecksFailed;
  const items: ListItem[] = [];

  if (data.RevisitDue) {
    items.push({ key: 'revisit', title: t(`${k}RevisitDue`), tone: 'warn', href: WIZARD_URL });
  }
  if (data.NextLabel) {
    items.push({ key: 'next', title: data.NextLabel, meta: t(`${k}Next`), icon: 'fa-circle-o', href: data.NextUrl || WIZARD_URL });
  }
  if (data.ChecksFailed) {
    items.push({ key: 'failed', title: t(`${k}ChecksFailedCount`, data.ChecksFailed), tone: 'bad', href: REPORT_URL });
  }
  if (data.NeedsAttention && size !== 'm') {
    items.push({ key: 'attention', title: t(`${k}NeedsAttentionCount`, data.NeedsAttention), tone: 'warn', href: REPORT_URL });
  }

  return (
    <>
      <HeroNumber
        size="md"
        value={
          <>
            {ctx.format.number(data.ChecksVerified, 0)}
            <span className="rg-mu" style={{ fontSize: 16 }}>
              {' '}
              / {ctx.format.number(data.ChecksRequired, 0)}
            </span>
          </>
        }
        label={t(`${k}ChecksLabel`)}
      />
      <BarList
        max={100}
        items={[{ label: t(`${k}Progress`), value: data.PercentComplete, display: ctx.format.percent(data.PercentComplete), tone: done ? 'good' : undefined, href: REPORT_URL }]}
      />
      {done && items.length === 0 ? (
        <ItemList items={[{ key: 'done', title: t(`${k}Done`), tone: 'good', href: REPORT_URL }]} />
      ) : (
        <ItemList items={items} />
      )}
      <Note>
        {t(`${k}TasksDone`, data.TasksDone, data.TasksTotal)}
        {' · '}
        <a href={data.NextUrl || WIZARD_URL}>{t(`${k}Continue`)}</a>
      </Note>
    </>
  );
}
