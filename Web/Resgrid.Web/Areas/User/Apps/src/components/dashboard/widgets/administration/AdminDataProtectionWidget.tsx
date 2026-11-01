import { BarList, ItemList, Note } from '../../primitives';
import type { ListItem, Tone } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Administration/AdminDataProtectionWidgetData.cs */
export interface AdminDataProtectionWidgetData {
  /** DepartmentDataProtectionState name. */
  State: string;
  Enforced: boolean;
  CatalogVersion: number;
  CatalogUpgradePending: boolean;
  StepUpWindowMinutes: number;
  OffboardingEffectiveOn?: string | null;
  MigrationRunning: boolean;
  MigrationKind?: string | null;
  PercentComplete?: number | null;
  RowsTotal: number;
  RowsCompleted: number;
  RowsAnomalous: number;
}

const PAGE_URL = '/User/DataProtection';

function stateTone(state: string): Tone | undefined {
  switch (state) {
    case 'Enabled':
      return 'good';
    case 'Failed':
      return 'bad';
    case 'OffboardingScheduled':
    case 'DisableRequested':
      return 'warn';
    case 'Disabled':
      return undefined;
    default:
      return 'info';
  }
}

/** admin.dataProtection (plan §7.11, §9A.5): Advanced Data Protection state and migration progress — never a protected value. */
export default function AdminDataProtectionWidget({ data, size, t, ctx }: WidgetProps<AdminDataProtectionWidgetData>) {
  const k = 'W.admin.dataProtection.';
  const items: ListItem[] = [
    {
      key: 'state',
      title: t(`${k}State.${data.State}`),
      meta: data.Enforced && data.StepUpWindowMinutes ? t(`${k}StepUpWindow`, data.StepUpWindowMinutes) : undefined,
      tone: stateTone(data.State),
      icon: data.State === 'Disabled' ? 'fa-shield' : undefined,
      href: PAGE_URL,
    },
  ];
  if (data.OffboardingEffectiveOn) {
    items.push({ key: 'offboarding', title: t(`${k}Offboarding`, ctx.format.date(data.OffboardingEffectiveOn)), tone: 'warn', href: PAGE_URL });
  }

  return (
    <>
      <ItemList items={items} />
      {data.MigrationRunning ? (
        <>
          <BarList
            max={100}
            items={[
              {
                label: data.MigrationKind ? t(`${k}MigrationKind`, data.MigrationKind) : t(`${k}Migration`),
                value: data.PercentComplete ?? 0,
                display: data.PercentComplete !== null && data.PercentComplete !== undefined ? ctx.format.percent(data.PercentComplete) : '—',
                href: PAGE_URL,
              },
            ]}
          />
          {size !== 's' ? (
            <Note>
              {t(`${k}Rows`, ctx.format.number(data.RowsCompleted, 0), ctx.format.number(data.RowsTotal, 0))}
              {data.RowsAnomalous ? ` · ${t(`${k}Anomalies`)} ${ctx.format.number(data.RowsAnomalous, 0)}` : ''}
            </Note>
          ) : null}
        </>
      ) : null}
      {data.CatalogUpgradePending ? (
        <Note>
          <i className="fa fa-info-circle" aria-hidden="true" /> {t(`${k}CatalogUpgrade`)}
        </Note>
      ) : null}
    </>
  );
}
