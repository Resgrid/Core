import { Agenda, EmptyState, StatusChip } from '../../primitives';
import type { WidgetProps } from '../types';
import { dayLabel } from './planningTime';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/CalendarAgendaWidgetData.cs */
export interface CalendarAgendaWidgetData {
  TimeZoneId?: string | null;
  Days: number;
  TotalCount: number;
  Items: Array<{
    CalendarItemId: number;
    Title?: string | null;
    Location?: string | null;
    StartUtc: string;
    EndUtc: string;
    IsAllDay: boolean;
    TypeId: number;
    TypeName?: string | null;
    TypeColor?: string | null;
    /** 0 none, 1 assigned, 2 RSVP. */
    SignupType: number;
    CheckInType: number;
    /** 1 RSVP, 2 required, 3 optional, 4 not attending. */
    MyAttendeeType?: number | null;
    CheckedIn: boolean;
    CheckedOut: boolean;
    Url: string;
  }>;
}

const ATTENDEE_KEY: Record<number, string> = { 1: 'Attending', 2: 'Required', 3: 'Optional', 4: 'NotAttending' };

/** calendar.agenda (plan §7.6): the next days of the department calendar with the viewer's sign-up and check-in state. */
export default function CalendarAgendaWidget({ data, t, ctx }: WidgetProps<CalendarAgendaWidgetData>) {
  if (data.Items.length === 0) {
    return <EmptyState icon="fa-calendar-o" text={t('W.calendar.agenda.Empty')} linkText={t('W.calendar.agenda.OpenCalendar')} href="/User/Calendar" />;
  }

  return (
    <Agenda
      items={data.Items.map((item) => {
        const state = item.CheckedIn
          ? t(item.CheckedOut ? 'W.calendar.agenda.CheckedOut' : 'W.calendar.agenda.CheckedIn')
          : item.MyAttendeeType
            ? t(`W.calendar.agenda.${ATTENDEE_KEY[item.MyAttendeeType] ?? 'Attending'}`)
            : null;
        const meta = [item.Location].filter(Boolean);
        return {
          key: String(item.CalendarItemId),
          day: dayLabel(item.StartUtc, ctx.format, t, 'W.calendar.agenda.Today', 'W.calendar.agenda.Tomorrow', true),
          time: item.IsAllDay ? t('W.calendar.agenda.AllDay') : ctx.format.time(item.StartUtc),
          title: item.Title || t('W.calendar.agenda.Untitled'),
          href: item.Url,
          meta:
            item.TypeName || meta.length > 0 ? (
              <>
                {item.TypeName ? <StatusChip label={item.TypeName} color={item.TypeColor} /> : null}
                {item.TypeName && meta.length > 0 ? ' ' : null}
                {meta.join(' · ')}
              </>
            ) : undefined,
          right: state ? (
            <span className="rg-m" style={{ whiteSpace: 'nowrap' }}>
              <i
                className={`fa ${item.MyAttendeeType === 4 && !item.CheckedIn ? 'fa-times-circle' : 'fa-check-circle'}`}
                aria-hidden="true"
                style={{ color: item.MyAttendeeType === 4 && !item.CheckedIn ? 'var(--rg-muted)' : 'var(--rg-good-text)' }}
              />{' '}
              {state}
            </span>
          ) : item.SignupType === 2 ? (
            <a className="rg-btn-xs" href={item.Url}>
              {t('W.calendar.agenda.SignUp')}
            </a>
          ) : undefined,
        };
      })}
    />
  );
}
