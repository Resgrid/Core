import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// awareness widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'map.operations': lazy(() => import('./MapOperationsWidget')),
  'map.visualization': lazy(() => import('./MapVisualizationWidget')),
  'weather.current': lazy(() => import('./WeatherCurrentWidget')),
  'weather.alerts': lazy(() => import('./WeatherAlertsWidget')),
  'general.clock': lazy(() => import('./ClockWidget')),
};

export default widgets;
