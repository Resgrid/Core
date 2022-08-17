import {
  Component,
  ChangeDetectionStrategy,
  ViewChild,
  TemplateRef,
  OnInit,
} from '@angular/core';
import {
  startOfDay,
  endOfDay,
  subDays,
  addDays,
  endOfMonth,
  isSameDay,
  isSameMonth,
  addHours,
} from 'date-fns';
import { Observable, Subject } from 'rxjs';
import {
  CalendarEvent,
  CalendarEventAction,
  CalendarEventTimesChangedEvent,
  CalendarView,
} from 'angular-calendar';
import { ShiftsService } from '@resgrid/ngx-resgridlib';
import { map, take } from 'rxjs/operators';
import { HttpClient } from '@angular/common/http';

const colors: any = {
  red: {
    primary: '#ad2121',
    secondary: '#FAE3E3',
  },
  blue: {
    primary: '#1e90ff',
    secondary: '#D1E8FF',
  },
  yellow: {
    primary: '#e3bc08',
    secondary: '#FDF1BA',
  },
};

@Component({
  templateUrl: './shifts-calendar.component.html',
  styleUrls: ['./shifts-calendar.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShiftsCalendarComponent implements OnInit {
  @ViewChild('modalContent', { static: true }) modalContent:
    | TemplateRef<any>
    | undefined;
  view: CalendarView = CalendarView.Month;
  CalendarView = CalendarView;
  viewDate: Date = new Date();
  refresh = new Subject<void>();
  activeDayIsOpen: boolean = true;
  modalData:
    | {
        action: string;
        event: CalendarEvent;
      }
    | undefined;
  actions: CalendarEventAction[] = [
    {
      label: '<i class="fas fa-fw fa-pencil-alt"></i>',
      a11yLabel: 'Edit',
      onClick: ({ event }: { event: CalendarEvent }): void => {
        this.handleEvent('Edited', event);
      },
    },
    {
      label: '<i class="fas fa-fw fa-trash-alt"></i>',
      a11yLabel: 'Delete',
      onClick: ({ event }: { event: CalendarEvent }): void => {
        //this.events = this.events.filter((iEvent) => iEvent !== event);
        this.handleEvent('Deleted', event);
      },
    },
  ];
  //public events: CalendarEvent[] = [];
  public events$: Observable<CalendarEvent<{ shift: any }>[]>;

  constructor(private shiftProvider: ShiftsService, private http: HttpClient) {
    this.events$ = this.http.get('/User/Shifts/GetShiftCalendarItems').pipe(
      map((results: any[]) => {
        if (results && results.length > 0) {
          return results.map((shift: any) => {
            if (shift) {
              return {
                title: shift.Title,
                start: new Date(shift.Start),
                end: new Date(shift.End),
                color: {
                  primary: shift.Color,
                  secondary: shift.Color,
                },
                allDay: shift.IsAllDay,
                draggable: false,
                resizable: {
                  beforeStart: false,
                  afterEnd: false,
                },
                meta: {
                  shift,
                },
              };
            }
          });
        }
      })
    );
  }

  ngOnInit(): void {
    /*
    this.shiftProvider
      .getShifts()
      .pipe(take(1))
      .subscribe((shifts) => {
        if (shifts && shifts.PageSize > 0) {
          this.events = shifts.Data.map((shift) => {
            return {
              title: shift.Name,
              start: new Date(shift.StartDate),
              end: new Date(shift.EndDate),
              color: {
                primary: shift.Color,
                secondary: shift.Color,
              },
              draggable: false,
              resizable: {
                beforeStart: false,
                afterEnd: false,
              },
            };
          });
        }
      });
      */
  }

  ngAfterContentInit() {
    /*
    this.http.get('/User/Shifts/GetShiftCalendarItems').subscribe((result: any) => {
      if (result) {
        this.events = result.map((shift) => {
          return {
            title: shift.Title,
            start: new Date(shift.Start),
            end: new Date(shift.End),
            color: {
              primary: shift.Color,
              secondary: shift.Color,
            },
            allDay: shift.IsAllDay,
            draggable: false,
            resizable: {
              beforeStart: false,
              afterEnd: false,
            },
          };
        });
      }
    });*/
  }

  dayClicked({ date, events }: { date: Date; events: CalendarEvent[] }): void {
    if (isSameMonth(date, this.viewDate)) {
      if (
        (isSameDay(this.viewDate, date) && this.activeDayIsOpen === true) ||
        events.length === 0
      ) {
        this.activeDayIsOpen = false;
      } else {
        this.activeDayIsOpen = true;
      }
      this.viewDate = date;
    }
  }

  handleEvent(action: string, event: CalendarEvent<{shift: any}>): void {
    if (action) {
      if (action === 'Clicked') {
        if (event && event.meta && event.meta.shift) {
          if (!event.meta.shift.WorkshiftId) {
            if (event.meta.shift.SignupType === 0) {
              window.open(
                `/User/Shifts/ViewShift?shiftDayId=${event.meta.shift.CalendarItemId}`, '_self'
              );
            } else {
              window.open(
                `/User/Shifts/Signup?shiftDayId=${event.meta.shift.CalendarItemId}`, '_self'
              );
            }
          } else {
            window.open(
              `/User/Workshifts/ViewDay?dayId=${event.meta.shift.WorkshiftDayId}`, '_self'
            );
          }
        }
      }
    }
  }

  setView(view: CalendarView) {
    this.view = view;
  }

  closeOpenMonthViewDay() {
    this.activeDayIsOpen = false;
  }
}
