import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// business widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'deployments.active': lazy(() => import('./DeploymentsActiveWidget')),
  'deployments.timeReports': lazy(() => import('./DeploymentTimeReportsWidget')),
  'invoicing.receivables': lazy(() => import('./InvoicingReceivablesWidget')),
  'invoicing.onlinePayments': lazy(() => import('./InvoicingOnlinePaymentsWidget')),
  'bids.pipeline': lazy(() => import('./BidsPipelineWidget')),
  'contracts.expiring': lazy(() => import('./ContractsExpiringWidget')),
  'costRecovery.queue': lazy(() => import('./CostRecoveryQueueWidget')),
  'workforce.fieldCosting': lazy(() => import('./WorkforceFieldCostingWidget')),
  'workforce.payData': lazy(() => import('./WorkforcePayDataWidget')),
};

export default widgets;
