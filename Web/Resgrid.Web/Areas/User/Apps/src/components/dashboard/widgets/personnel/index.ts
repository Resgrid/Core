import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// personnel widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'personnel.myStatus': lazy(() => import('./PersonnelMyStatusWidget')),
  'personnel.board': lazy(() => import('./PersonnelBoardWidget')),
  'personnel.statusSummary': lazy(() => import('./PersonnelStatusSummaryWidget')),
  'personnel.staffingSummary': lazy(() => import('./PersonnelStaffingSummaryWidget')),
};

export default widgets;
