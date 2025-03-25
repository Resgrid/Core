// This file can be replaced during build by using the `fileReplacements` array.
// `ng build` replaces `environment.ts` with `environment.prod.ts`.
// The list of file replacements can be found in `angular.json`.

export const environment = {
  production: false,
  baseApiUrl: 'https://qaapi.resgrid.com',
  resgridApiUrl: '/api/v4',
  channelUrl: 'https://qaevents.resgrid.com/',
  channelHubName: 'eventingHub',
  logLevel: 0,
  what3WordsKey: 'W3WKEY',
  isDemo: false,
  demoToken: 'DEMOTOKEN',
  version: '99.99.99',
  osmMapKey: '',
  mapTilerKey: '',
  googleMapsKey: 'GOOGLEMAPKEY',
  loggingKey: ''
};

/*
 * For easier debugging in development mode, you can import the following file
 * to ignore zone related error stack frames such as `zone.run`, `zoneDelegate.invokeTask`.
 *
 * This import should be commented out in production mode because it will have a negative impact
 * on performance if an error is thrown.
 */
// import 'zone.js/plugins/zone-error';  // Included with Angular CLI.
