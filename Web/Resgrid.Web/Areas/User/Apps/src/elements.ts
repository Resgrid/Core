import './styles/base.css';
import { defineReactElement, type PropDefinition } from './runtime/customElement';
import type { MapElementProps } from './components/map/MapElement';
import type { ShiftsCalendarElementProps } from './components/shifts/ShiftsCalendarElement';
import type { OmnibarElementProps } from './components/omnibar/OmnibarElement';

const mapProps: readonly PropDefinition[] = [
  { attribute: 'showbuttons', property: 'showButtons', type: 'boolean', defaultValue: false },
  { attribute: 'mapheight', property: 'mapHeight', type: 'string', defaultValue: '600px' },
  { attribute: 'departmentid', property: 'departmentId', type: 'number' },
  { attribute: 'leafletosmurl', property: 'leafletOsmUrl', type: 'string' },
  { attribute: 'mapattribution', property: 'mapAttribution', type: 'string', defaultValue: '' },
  { attribute: 'mapprovider', property: 'mapProvider', type: 'string', defaultValue: '' },
  { attribute: 'mapstyleurl', property: 'mapStyleUrl', type: 'string', defaultValue: '' },
  { attribute: 'mapaccesstoken', property: 'mapAccessToken', type: 'string', defaultValue: '' },
  { attribute: 'mapconfig', property: 'mapConfig', type: 'json' },
];

const omnibarProps: readonly PropDefinition[] = [
  { attribute: 'title', property: 'title', type: 'string', defaultValue: '' },
  { attribute: 'placeholder', property: 'placeholder', type: 'string', defaultValue: 'Search' },
  { attribute: 'items', property: 'items', type: 'json', defaultValue: [] },
  { attribute: 'showcount', property: 'showCount', type: 'boolean', defaultValue: true },
  { attribute: 'autofocus', property: 'autoFocus', type: 'boolean', defaultValue: false },
  { attribute: 'emptytext', property: 'emptyText', type: 'string', defaultValue: 'No matching results' },
  { attribute: 'maxitems', property: 'maxItems', type: 'number', defaultValue: 8 },
];

defineReactElement<MapElementProps>(
  'rg-map',
  () => import('./components/map/MapElement'),
  mapProps,
);

defineReactElement<ShiftsCalendarElementProps>(
  'rg-shifts-calendar',
  () => import('./components/shifts/ShiftsCalendarElement'),
  [],
);

defineReactElement<OmnibarElementProps>(
  'rg-omnibar',
  () => import('./components/omnibar/OmnibarElement'),
  omnibarProps,
);
