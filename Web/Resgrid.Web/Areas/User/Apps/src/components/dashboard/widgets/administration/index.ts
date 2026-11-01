import { lazy } from 'react';
import type { WidgetRegistry } from '../types';

// administration widgets. One line per widget key (home-dashboard-implementation-contract.md §4).
const widgets: WidgetRegistry = {
  'department.info': lazy(() => import('./DepartmentInfoWidget')),
  'admin.setup': lazy(() => import('./AdminSetupWidget')),
  'admin.plan': lazy(() => import('./AdminPlanWidget')),
  'admin.workflows': lazy(() => import('./AdminWorkflowsWidget')),
  'admin.dataProtection': lazy(() => import('./AdminDataProtectionWidget')),
  'admin.import': lazy(() => import('./AdminImportWidget')),
  'admin.security': lazy(() => import('./AdminSecurityWidget')),
};

export default widgets;
