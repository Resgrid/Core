import { Fragment } from 'react';
import { EmptyState, KpiGrid, SubHeading, type KpiItem } from '../../primitives';
import type { WidgetProps } from '../types';
import { toneIf } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsPreventionWidgetData.cs */
export interface RecordsPreventionWidgetData {
  ShowOccupancies: boolean;
  Occupancies: number;
  OccupancyReviewsOverdue: number;
  ShowInspections: boolean;
  InspectionsScheduled: number;
  InspectionsDue: number;
  ViolationsOpen: number;
  ViolationsOverdue: number;
  ShowHydrants: boolean;
  Hydrants: number;
  HydrantsOutOfService: number;
  HydrantsTestDue: number;
  ShowPermits: boolean;
  PermitsActive: number;
  PermitsExpiringSoon: number;
  PermitsAwaitingReview: number;
  ShowCrr: boolean;
  CrrActivitiesLast90Days: number;
  CrrAlarmsLast90Days: number;
  ShowInvestigations: boolean;
  InvestigationCasesOpen: number;
}

interface Group {
  key: string;
  label: string;
  items: KpiItem[];
}

/** records.prevention (plan §7.8): prevention workload, one tile group per module the department uses. */
export default function RecordsPreventionWidget({ data, size, t, ctx }: WidgetProps<RecordsPreventionWidgetData>) {
  const k = 'W.records.prevention.';
  const n = (value: number) => ctx.format.number(value, 0);
  const groups: Group[] = [];

  if (data.ShowOccupancies) {
    groups.push({ key: 'occupancies', label: t(`${k}Occupancies`), items: [
      { value: n(data.Occupancies), label: t(`${k}OccupanciesCount`), href: '/User/RecordOccupancies' },
      { value: n(data.OccupancyReviewsOverdue), label: t(`${k}ReviewsOverdue`), tone: toneIf(data.OccupancyReviewsOverdue, 'bad'), href: '/User/RecordOccupancies' },
    ] });
  }
  if (data.ShowInspections) {
    groups.push({ key: 'inspections', label: t(`${k}Inspections`), items: [
      { value: n(data.InspectionsDue), label: t(`${k}InspectionsDue`), tone: toneIf(data.InspectionsDue, 'warn'), href: '/User/RecordInspections' },
      { value: n(data.InspectionsScheduled), label: t(`${k}InspectionsScheduled`), href: '/User/RecordInspections' },
      { value: n(data.ViolationsOpen), label: t(`${k}ViolationsOpen`), href: '/User/RecordInspections' },
      { value: n(data.ViolationsOverdue), label: t(`${k}ViolationsOverdue`), tone: toneIf(data.ViolationsOverdue, 'bad'), href: '/User/RecordInspections' },
    ] });
  }
  if (data.ShowHydrants) {
    groups.push({ key: 'hydrants', label: t(`${k}Hydrants`), items: [
      { value: n(data.HydrantsOutOfService), label: t(`${k}OutOfService`), tone: toneIf(data.HydrantsOutOfService, 'bad'), href: '/User/RecordHydrants' },
      { value: n(data.HydrantsTestDue), label: t(`${k}TestDue`), tone: toneIf(data.HydrantsTestDue, 'warn'), href: '/User/RecordHydrants' },
      { value: n(data.Hydrants), label: t(`${k}HydrantsCount`), href: '/User/RecordHydrants' },
    ] });
  }
  if (data.ShowPermits) {
    groups.push({ key: 'permits', label: t(`${k}Permits`), items: [
      { value: n(data.PermitsAwaitingReview), label: t(`${k}AwaitingReview`), href: '/User/RecordPermits' },
      { value: n(data.PermitsExpiringSoon), label: t(`${k}ExpiringSoon`), tone: toneIf(data.PermitsExpiringSoon, 'warn'), href: '/User/RecordPermits' },
      { value: n(data.PermitsActive), label: t(`${k}PermitsActive`), href: '/User/RecordPermits' },
    ] });
  }
  if (data.ShowCrr) {
    groups.push({ key: 'crr', label: t(`${k}Crr`), items: [
      { value: n(data.CrrActivitiesLast90Days), label: t(`${k}Activities90`), href: '/User/RecordCrr' },
      { value: n(data.CrrAlarmsLast90Days), label: t(`${k}Alarms90`), href: '/User/RecordCrr' },
    ] });
  }
  if (data.ShowInvestigations) {
    groups.push({ key: 'investigations', label: t(`${k}Investigations`), items: [
      { value: n(data.InvestigationCasesOpen), label: t(`${k}CasesOpen`), href: '/User/RecordInvestigations' },
    ] });
  }

  if (!groups.length) {
    return <EmptyState icon="fa-building-o" text={t(`${k}NoModules`)} linkText={t(`${k}OpenRecords`)} href="/User/Records/Dashboard" />;
  }

  // At m the tiles share one grid (the module label moves into a tooltip); larger sizes group them under headings.
  if (size === 'm') {
    return <KpiGrid items={groups.flatMap((group) => group.items.slice(0, 2).map((item) => ({ ...item, title: `${group.label}: ${item.label}` })))} />;
  }

  return (
    <>
      {groups.map((group) => (
        <Fragment key={group.key}>
          <SubHeading>{group.label}</SubHeading>
          <KpiGrid items={group.items} />
        </Fragment>
      ))}
    </>
  );
}
