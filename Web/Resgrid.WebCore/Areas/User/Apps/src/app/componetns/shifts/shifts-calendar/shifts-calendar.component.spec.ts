import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ShiftsCalendarComponent } from './shifts-calendar.component';

describe('ShiftsCalendarComponent', () => {
  let component: ShiftsCalendarComponent;
  let fixture: ComponentFixture<ShiftsCalendarComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ ShiftsCalendarComponent ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(ShiftsCalendarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
