import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// readiness widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'checklists.due': lazy(() => import('./ChecklistsDueWidget')),
  'checklists.compliance': lazy(() => import('./ChecklistsComplianceWidget')),
  'workOrders.stats': lazy(() => import('./WorkOrdersStatsWidget')),
  'workOrders.mine': lazy(() => import('./WorkOrdersMineWidget')),
  'inventory.alerts': lazy(() => import('./InventoryAlertsWidget')),
  'inventory.valuation': lazy(() => import('./InventoryValuationWidget')),
  'certifications.expiry': lazy(() => import('./CertificationsExpiryWidget')),
  'certifications.mine': lazy(() => import('./CertificationsMineWidget')),
};

export default widgets;
