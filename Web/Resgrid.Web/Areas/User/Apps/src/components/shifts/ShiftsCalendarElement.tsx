import { useEffect, useState } from 'react';
import {
  Calendar,
  Views,
  dateFnsLocalizer,
} from 'react-big-calendar';
import format from 'date-fns/format';
import getDay from 'date-fns/getDay';
import parse from 'date-fns/parse';
import startOfWeek from 'date-fns/startOfWeek';
import enUS from 'date-fns/locale/en-US';
import 'react-big-calendar/lib/css/react-big-calendar.css';
import LoadingIndicator from '../shared/LoadingIndicator';
import { siteFetchJson } from '../../runtime/api';
import './shifts.css';

const locales = {
  'en-US': enUS,
};

const localizer = dateFnsLocalizer({
  format,
  parse,
  startOfWeek,
  getDay,
  locales,
});

interface ShiftCalendarItem {
  CalendarItemId: number;
  Title: string;
  Start: string;
  End: string;
  Color: string;
  SignupType: number;
  IsAllDay: boolean;
  ShiftId: number;
  WorkshiftId?: string;
  WorkshiftDayId?: string;
  Filled: boolean;
  UserSignedUp: boolean;
}

interface ShiftCalendarEvent {
  title: string;
  start: Date;
  end: Date;
  allDay: boolean;
  resource: ShiftCalendarItem;
}

export interface ShiftsCalendarElementProps {
  hostElement?: HTMLElement;
}

function navigateToShift(resource: ShiftCalendarItem) {
  if (!resource.WorkshiftId) {
    if (resource.SignupType === 0) {
      window.location.assign(`/User/Shifts/ViewShift?shiftDayId=${resource.CalendarItemId}`);
      return;
    }

    window.location.assign(`/User/Shifts/Signup?shiftDayId=${resource.CalendarItemId}`);
    return;
  }

  window.location.assign(`/User/Workshifts/ViewDay?dayId=${resource.WorkshiftDayId}`);
}

export default function ShiftsCalendarElement(_: ShiftsCalendarElementProps) {
  const [events, setEvents] = useState<ShiftCalendarEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const loadEventsAsync = async () => {
      setLoading(true);
      setError(null);

      try {
        const response = await siteFetchJson<ShiftCalendarItem[]>('/User/Shifts/GetShiftCalendarItems');

        if (cancelled) {
          return;
        }

        setEvents(
          response.map((shift) => ({
            title: shift.Title,
            start: new Date(shift.Start),
            end: new Date(shift.End),
            allDay: shift.IsAllDay,
            resource: shift,
          })),
        );
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : 'Unable to load shifts.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void loadEventsAsync();

    return () => {
      cancelled = true;
    };
  }, []);

  if (loading) {
    return <LoadingIndicator label="Loading shift calendar..." />;
  }

  if (error) {
    return <div className="rg-error">{error}</div>;
  }

  return (
    <div className="rg-shifts">
      <div className="rg-shifts__calendar">
        <Calendar<ShiftCalendarEvent>
          localizer={localizer}
          events={events}
          defaultView={Views.MONTH}
          views={[Views.MONTH, Views.WEEK, Views.DAY]}
          popup
          messages={{
            today: 'Today',
            previous: 'Previous',
            next: 'Next',
            month: 'Month',
            week: 'Week',
            day: 'Day',
          }}
          eventPropGetter={(event: ShiftCalendarEvent) => ({
            style: {
              backgroundColor: event.resource.Color,
              borderColor: event.resource.Color,
              color: '#111827',
              borderRadius: '4px',
            },
          })}
          onSelectEvent={(event: ShiftCalendarEvent) => navigateToShift(event.resource)}
        />
      </div>
    </div>
  );
}
