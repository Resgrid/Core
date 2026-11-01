import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// incidentBackOffice widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'ibo.activations': lazy(() => import('./IncidentActivationsWidget')),
  'ibo.desk': lazy(() => import('./IncidentDeskWidget')),
  'ibo.resources': lazy(() => import('./IncidentResourcesWidget')),
  'ibo.checkIn': lazy(() => import('./IncidentCheckInWidget')),
  'ibo.finance': lazy(() => import('./IncidentFinanceWidget')),
  'ibo.coordination': lazy(() => import('./IncidentCoordinationWidget')),
};

export default widgets;
