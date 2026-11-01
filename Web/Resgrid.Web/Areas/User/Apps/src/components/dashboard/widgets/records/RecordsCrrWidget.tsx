import { BarList, EmptyState, HeroNumber, KpiGrid } from '../../primitives';
import type { WidgetProps } from '../types';
import { isCompact, toneIf } from '../readiness/readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Records/RecordsCrrWidgetData.cs */
export interface RecordsCrrWidgetData {
  StartUtc: string;
  EndUtc: string;
  Activities: number;
  Audience: number;
  SmokeAlarmsInstalled: number;
  CoAlarmsInstalled: number;
  ComboAlarmsInstalled: number;
  AlarmsInstalled: number;
  AlarmVisits: number;
  OpenAlarmRequests: number;
}

const CRR_URL = '/User/RecordCrr';
const REQUESTS_URL = '/User/RecordAlarmRequests';

/** records.crr (plan §7.8): community risk reduction this year and the open smoke/CO alarm requests. */
export default function RecordsCrrWidget({ data, size, t, ctx }: WidgetProps<RecordsCrrWidgetData>) {
  const k = 'W.records.crr.';
  if (!data.Activities && !data.AlarmsInstalled && !data.OpenAlarmRequests) {
    return <EmptyState icon="fa-bullhorn" text={t(`${k}Empty`)} linkText={t(`${k}OpenCrr`)} href={CRR_URL} />;
  }

  const n = (value: number) => ctx.format.number(value, 0);
  return (
    <>
      <HeroNumber value={n(data.AlarmsInstalled)} label={t(`${k}AlarmsThisYear`)} icon="fa-bell-o" size="md" />
      <KpiGrid
        columns={2}
        items={[
          { value: n(data.Activities), label: t(`${k}Activities`), href: CRR_URL },
          { value: n(data.OpenAlarmRequests), label: t(`${k}OpenRequests`), tone: toneIf(data.OpenAlarmRequests, 'warn'), href: REQUESTS_URL },
        ]}
      />
      {!isCompact(size) ? (
        <BarList
          items={[
            { label: t(`${k}Smoke`), value: data.SmokeAlarmsInstalled },
            { label: t(`${k}Co`), value: data.CoAlarmsInstalled },
            { label: t(`${k}Combo`), value: data.ComboAlarmsInstalled },
          ]}
        />
      ) : null}
    </>
  );
}
