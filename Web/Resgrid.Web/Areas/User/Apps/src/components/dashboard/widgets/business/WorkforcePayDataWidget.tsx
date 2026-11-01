import { BarList, HeroNumber, KpiGrid, Note } from '../../primitives';
import type { WidgetProps } from '../types';
import { isCompact, toneIf } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/WorkforcePayDataWidgetData.cs */
export interface WorkforcePayDataWidgetData {
  ReportingYear: number;
  ActiveWorkers: number;
  WithResponse: number;
  Missing: number;
  CompletenessPercent: number;
  DueDate?: string | null;
  ProfileAvailable: boolean;
  OpenRuns: number;
  UnresolvedExceptions: number;
  AnnualFactsMissing: number;
  HasFrozenRun: boolean;
  HasCertifiedRun: boolean;
}

const PAY_DATA_URL = '/User/Workforce/PayData';

/** workforce.payData (plan §7.9): California pay data completeness for the last reporting year (counts only). */
export default function WorkforcePayDataWidget({ data, size, t, ctx }: WidgetProps<WorkforcePayDataWidgetData>) {
  const k = 'W.workforce.payData.';
  const compact = isCompact(size);

  return (
    <>
      <HeroNumber
        size={compact ? 'md' : 'lg'}
        value={ctx.format.percent(data.CompletenessPercent)}
        label={t(`${k}Responses`, data.WithResponse, data.ActiveWorkers)}
        icon={data.HasCertifiedRun ? 'fa-check-circle' : undefined}
      />
      {!compact ? (
        <>
          <BarList
            max={100}
            items={[
              {
                label: t(`${k}ReportingYear`, data.ReportingYear),
                value: data.CompletenessPercent,
                display: ctx.format.percent(data.CompletenessPercent),
                tone: data.CompletenessPercent < 100 ? 'warn' : 'good',
                href: PAY_DATA_URL,
              },
            ]}
          />
          <KpiGrid
            columns={3}
            items={[
              { value: ctx.format.number(data.OpenRuns, 0), label: t(`${k}OpenRuns`), href: PAY_DATA_URL },
              { value: ctx.format.number(data.UnresolvedExceptions, 0), label: t(`${k}Exceptions`), tone: toneIf(data.UnresolvedExceptions, 'warn') },
              { value: ctx.format.number(data.AnnualFactsMissing, 0), label: t(`${k}AnnualFactsMissing`), tone: toneIf(data.AnnualFactsMissing, 'warn') },
            ]}
          />
        </>
      ) : null}
      <Note>
        {[data.DueDate ? t(`${k}Due`, ctx.format.date(data.DueDate)) : null, data.HasCertifiedRun ? t(`${k}Certified`) : null, !data.ProfileAvailable ? t(`${k}NoProfile`) : null]
          .filter(Boolean)
          .join(' · ') || t(`${k}ReportingYear`, data.ReportingYear)}
      </Note>
    </>
  );
}
