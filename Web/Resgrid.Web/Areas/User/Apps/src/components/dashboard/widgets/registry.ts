import administration from './administration';
import awareness from './awareness';
import business from './business';
import communications from './communications';
import incidentBackOffice from './incidentBackOffice';
import intelligence from './intelligence';
import operations from './operations';
import personnel from './personnel';
import planning from './planning';
import readiness from './readiness';
import records from './records';
import type { WidgetRegistry } from './types';

// Widget key → lazily loaded component (one chunk per widget). A key in the layout with no entry here renders the
// frame's neutral "not available yet" body, not an error. Add widgets in the category's index.ts, not here.
const categories: WidgetRegistry[] = [
  operations,
  personnel,
  awareness,
  communications,
  planning,
  readiness,
  records,
  business,
  incidentBackOffice,
  intelligence,
  administration,
];

export const widgetRegistry: WidgetRegistry = Object.assign({}, ...categories);

export function widgetComponent(key: string) {
  return Object.prototype.hasOwnProperty.call(widgetRegistry, key) ? widgetRegistry[key] : undefined;
}
