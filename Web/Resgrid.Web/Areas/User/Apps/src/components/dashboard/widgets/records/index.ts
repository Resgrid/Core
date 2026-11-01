import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// records widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'records.overview': lazy(() => import('./RecordsOverviewWidget')),
  'records.myWork': lazy(() => import('./RecordsMyWorkWidget')),
  'records.neris': lazy(() => import('./RecordsNerisWidget')),
  'records.qualityReview': lazy(() => import('./RecordsQualityReviewWidget')),
  'records.prevention': lazy(() => import('./RecordsPreventionWidget')),
  'records.hydrants': lazy(() => import('./RecordsHydrantsWidget')),
  'records.crr': lazy(() => import('./RecordsCrrWidget')),
  'records.investigations': lazy(() => import('./RecordsInvestigationsWidget')),
  'records.analytics': lazy(() => import('./RecordsAnalyticsWidget')),
  'records.legalHolds': lazy(() => import('./RecordsLegalHoldsWidget')),
  'records.deployments': lazy(() => import('./RecordsDeploymentsWidget')),
  'logs.recent': lazy(() => import('./LogsRecentWidget')),
};

export default widgets;
