import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// operations widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'calls.active': lazy(() => import('./CallsActiveWidget')),
  'calls.kpis': lazy(() => import('./CallsKpisWidget')),
  'calls.pending': lazy(() => import('./CallsPendingWidget')),
  'calls.awaitingResources': lazy(() => import('./CallsAwaitingResourcesWidget')),
  'calls.myCheckIns': lazy(() => import('./CallsMyCheckInsWidget')),
  'calls.volume': lazy(() => import('./CallsVolumeWidget')),
  'incidentCommand.active': lazy(() => import('./IncidentCommandActiveWidget')),
  'units.status': lazy(() => import('./UnitsStatusWidget')),
  'units.summary': lazy(() => import('./UnitsSummaryWidget')),
  'units.alerts': lazy(() => import('./UnitsAlertsWidget')),
  'units.routes': lazy(() => import('./UnitsRoutesWidget')),
};

export default widgets;
