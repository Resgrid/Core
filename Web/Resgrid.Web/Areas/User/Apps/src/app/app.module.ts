import { Injector, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { createCustomElement } from '@angular/elements';
import { AppComponent } from './app.component';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { CalendarModule, DateAdapter } from 'angular-calendar';
import { adapterFactory } from 'angular-calendar/date-adapters/date-fns';
import { ShiftsCalendarComponent } from './componetns/shifts/shifts-calendar/shifts-calendar.component';
import { NgxResgridLibModule } from '@resgrid/ngx-resgridlib';
import { CommonModule } from '@angular/common';
import { HttpClientModule } from '@angular/common/http';
import { MapComponent } from './componetns/map/map.component';
import { OverlayModule } from "@angular/cdk/overlay";
import { FormsModule } from '@angular/forms';

const getBaseUrl = (): string => {
  if (window['rgApiBaseUrl'] && window['rgApiBaseUrl'].length > 0) {
		return window['rgApiBaseUrl'].trim().toString();
	}
	return 'https://api.resgrid.com';
};

const getGoogleMapKey = (): string => {
  if (window['rgGoogleMapsKey'] && window['rgGoogleMapsKey'].length > 0) {
		return window['rgGoogleMapsKey'].trim().toString();
	}
	return '';
};

const getEventingUrl = (): string => {
  if (window['rgChannelUrl'] && window['rgChannelUrl'].length > 0) {
		return window['rgChannelUrl'].trim().toString();
	}
	return 'https://events.resgrid.com';
};

@NgModule({
  declarations: [
    AppComponent,
    ShiftsCalendarComponent,
    MapComponent
  ],
  imports: [
    CommonModule,
		HttpClientModule,
    BrowserModule,
    FormsModule,
    BrowserAnimationsModule,
    CalendarModule.forRoot({
      provide: DateAdapter,
      useFactory: adapterFactory,
    }),
    OverlayModule,
    NgxResgridLibModule.forRoot({
			baseApiUrl: getBaseUrl,
			apiVersion: 'v4',
			clientId: 'RgWebApp',
			googleApiKey: getGoogleMapKey(),
			channelUrl: getEventingUrl(),
			channelHubName: 'eventingHub',
      realtimeGeolocationHubName: '/geolocationHub',
			logLevel: 0,
			isMobileApp: false,
      cacheProvider: null,
      storageProvider: null
		})
  ],
  providers: [],
  exports: [
    ShiftsCalendarComponent,
    MapComponent
  ]
})
export class AppModule {
  constructor(private injector: Injector) {

  }

  ngDoBootstrap() {
    customElements.define('rg-shifts-calendar', createCustomElement(ShiftsCalendarComponent, { injector: this.injector }));
    customElements.define('rg-map', createCustomElement(MapComponent, { injector: this.injector }));
  }
}
