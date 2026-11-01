import { BarList, Note, SubHeading } from '../../primitives';
import type { BarItem, Tone } from '../../primitives';
import type { WidgetProps, WidgetFormat, Translate } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Administration/AdminPlanWidgetData.cs */
export interface AdminPlanWidgetData {
  PlanId: number;
  PlanName?: string | null;
  PlanUnavailable: boolean;
  IsFree: boolean;
  IsEntityPlan: boolean;
  PersonnelCount: number;
  PersonnelLimit: number;
  UnitsCount: number;
  UnitsLimit: number;
  EntityCount: number;
  EntityLimit: number;
  UsedPercent?: number | null;
  CanManageBilling: boolean;
  /** Billing figures: present only for a member who may manage the subscription. */
  Cost?: number | null;
  Frequency?: string | null;
  RenewsOn?: string | null;
  Cancelled: boolean;
  Addons: AdminPlanAddon[];
}

export interface AdminPlanAddon {
  /** PlanAddonTypes name. */
  Key: string;
  Offered: boolean;
  Held: boolean;
  Cancelled: boolean;
  EndsOn?: string | null;
}

const SUBSCRIPTION_URL = '/User/Subscription';
const UNLIMITED = 2147483647;

function usageTone(percent: number): Tone | undefined {
  if (percent >= 100) return 'bad';
  if (percent >= 90) return 'warn';
  return undefined;
}

function limitBar(label: string, used: number, limit: number, t: Translate, format: WidgetFormat): BarItem {
  if (limit <= 0 || limit >= UNLIMITED) {
    return { label, value: 0, display: `${format.number(used, 0)} · ${t('W.admin.plan.Unlimited')}` };
  }
  const percent = Math.round((used * 100) / limit);
  return { label, value: Math.min(100, percent), display: `${format.number(used, 0)} / ${format.number(limit, 0)}`, tone: usageTone(percent), href: SUBSCRIPTION_URL };
}

/** admin.plan (plan §7.11): the plan, its limits used and add-ons held or offered; billing figures only when the server sends them. */
export default function AdminPlanWidget({ data, size, t, ctx }: WidgetProps<AdminPlanWidgetData>) {
  const k = 'W.admin.plan.';
  const name = data.PlanName || t(`${k}FreePlan`);
  const billing: string[] = [];
  if (data.Cost !== null && data.Cost !== undefined && !data.IsFree) {
    billing.push(data.Frequency ? t(`${k}CostPer`, ctx.format.currency(data.Cost), t(`${k}Frequency.${data.Frequency}`)) : ctx.format.currency(data.Cost));
  }
  if (data.CanManageBilling && !data.IsFree) {
    billing.push(data.RenewsOn ? t(data.Cancelled ? `${k}Ends` : `${k}Renews`, ctx.format.date(data.RenewsOn)) : t(`${k}NeverExpires`));
  }

  const bars = data.IsEntityPlan
    ? [limitBar(t(`${k}Entities`), data.EntityCount, data.EntityLimit, t, ctx.format)]
    : [limitBar(t(`${k}Personnel`), data.PersonnelCount, data.PersonnelLimit, t, ctx.format), limitBar(t(`${k}Units`), data.UnitsCount, data.UnitsLimit, t, ctx.format)];
  const addons = (data.Addons ?? []).filter((addon) => addon.Held || addon.Offered);

  return (
    <>
      <div className="rg-m" style={{ color: 'var(--rg-heading)', marginBottom: 8 }}>
        <a href={SUBSCRIPTION_URL}>
          <b>{name}</b>
        </a>
        {billing.length > 0 ? <span className="rg-mu"> · {billing.join(' · ')}</span> : null}
      </div>
      {data.PlanUnavailable ? (
        <Note>
          <i className="fa fa-info-circle" aria-hidden="true" /> {t(`${k}PlanUnavailable`)}
        </Note>
      ) : null}
      <BarList max={100} items={bars} />
      {addons.length > 0 && size !== 'xs' && size !== 's' ? (
        <>
          <SubHeading>{t(`${k}Addons`)}</SubHeading>
          <div>
            {addons.map((addon) => (
              <span key={addon.Key} className="rg-tag" style={{ marginBottom: 4 }}>
                {addon.Held ? <i className="fa fa-check" aria-hidden="true" style={{ color: 'var(--rg-good-text)' }} /> : null} {t(`${k}Addon.${addon.Key}`)}
                {' · '}
                {addon.Held ? (
                  addon.Cancelled ? (
                    <span className="rg-mu">{addon.EndsOn ? t(`${k}Ends`, ctx.format.date(addon.EndsOn)) : t(`${k}Cancelled`)}</span>
                  ) : (
                    <span className="rg-mu">{t(`${k}Held`)}</span>
                  )
                ) : (
                  <a href={SUBSCRIPTION_URL}>{t(`${k}Available`)}</a>
                )}
              </span>
            ))}
          </div>
        </>
      ) : null}
    </>
  );
}
