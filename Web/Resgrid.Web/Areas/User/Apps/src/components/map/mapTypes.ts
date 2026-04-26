import type { FeatureCollection, GeoJsonProperties } from 'geojson';

export const mapMarkerTypes = {
  call: 0,
  unit: 1,
  station: 2,
  personnel: 3,
  poi: 4,
} as const;

export interface MapMarkerInfo {
  Id: string;
  Longitude: number;
  Latitude: number;
  Title: string;
  zIndex: number;
  ImagePath: string;
  InfoWindowContent: string;
  Color: string;
  Type: number | string;
  Marker?: string;
  PoiTypeId?: number | null;
  PoiTypeName?: string;
  Address?: string;
  Note?: string;
  LayerId?: string;
  LayerName?: string;
}

export interface PoiLayerInfo {
  PoiTypeId: number;
  Name: string;
  Color: string;
  ImagePath: string;
  Marker: string;
  IsDestination: boolean;
}

export interface GetMapDataResult {
  Data: {
    CenterLat: number;
    CenterLon: number;
    ZoomLevel: number;
    MapMakerInfos: MapMarkerInfo[];
    PoiLayers?: PoiLayerInfo[];
  };
}

export interface MapLayerInfo {
  Id: string;
  Name: string;
  Color: string;
  IsOnByDefault: boolean;
  Data?: {
    Features?: FeatureCollection;
  };
}

export interface GetMapLayersResult {
  Data: {
    Layers?: MapLayerInfo[];
  };
}

export interface MapConfigPayload {
  MapProvider?: string;
  TileUrl?: string;
  StyleUrl?: string;
  AccessToken?: string;
  Attribution?: string;
  IsDepartmentOverride?: boolean;
  mapProvider?: string;
  tileUrl?: string;
  styleUrl?: string;
  accessToken?: string;
  attribution?: string;
  isDepartmentOverride?: boolean;
}

export interface ResolvedMapConfig {
  mapProvider: string;
  tileUrl: string;
  styleUrl: string;
  accessToken: string;
  attribution: string;
  isDepartmentOverride: boolean;
}

export interface NormalizedMapLayer {
  id: string;
  name: string;
  color: string;
  isOnByDefault: boolean;
  featureCollection: FeatureCollection;
}

export interface MapElementProps {
  showButtons?: boolean;
  mapHeight?: string;
  departmentId?: number;
  leafletOsmUrl?: string;
  mapAttribution?: string;
  mapProvider?: string;
  mapStyleUrl?: string;
  mapAccessToken?: string;
  mapConfig?: MapConfigPayload | null;
  hostElement?: HTMLElement;
}

export interface MapRendererProps {
  mapData: GetMapDataResult['Data'] | null;
  markers: MapMarkerInfo[];
  layers: NormalizedMapLayer[];
  layerVisibility: Record<string, boolean>;
  poiLayers: PoiLayerInfo[];
  poiLayerVisibility: Record<string, boolean>;
  hideLabels: boolean;
  resolvedMapConfig: ResolvedMapConfig;
  fitBoundsKey: string;
}

const defaultPoiMarkerShape = 'MAP_PIN';

const poiMarkerPaths: Record<string, string> = {
  MAP_PIN: 'M0-48c-9.8 0-17.7 7.8-17.7 17.4 0 15.5 17.7 30.6 17.7 30.6s17.7-15.4 17.7-30.6c0-9.6-7.9-17.4-17.7-17.4z',
  SHIELD: 'M18.8-31.8c.3-3.4 1.3-6.6 3.2-9.5l-7-6.7c-2.2 1.8-4.8 2.8-7.6 3-2.6.2-5.1-.2-7.5-1.4-2.4 1.1-4.9 1.6-7.5 1.4-2.7-.2-5.1-1.1-7.3-2.7l-7.1 6.7c1.7 2.9 2.7 6 2.9 9.2.1 1.5-.3 3.5-1.3 6.1-.5 1.5-.9 2.7-1.2 3.8-.2 1-.4 1.9-.5 2.5 0 2.8.8 5.3 2.5 7.5 1.3 1.6 3.5 3.4 6.5 5.4 3.3 1.6 5.8 2.6 7.6 3.1.5.2 1 .4 1.5.7l1.5.6c1.2.7 2 1.4 2.4 2.1.5-.8 1.3-1.5 2.4-2.1.7-.3 1.3-.5 1.9-.8.5-.2.9-.4 1.1-.5.4-.1.9-.3 1.5-.6.6-.2 1.3-.5 2.2-.8 1.7-.6 3-1.1 3.8-1.6 2.9-2 5.1-3.8 6.4-5.3 1.7-2.2 2.6-4.8 2.5-7.6-.1-1.3-.7-3.3-1.7-6.1-.9-2.8-1.3-4.9-1.2-6.4z',
  ROUTE: 'M24-28.3c-.2-13.3-7.9-18.5-8.3-18.7l-1.2-.8-1.2.8c-2 1.4-4.1 2-6.1 2-3.4 0-5.8-1.9-5.9-1.9l-1.3-1.1-1.3 1.1c-.1.1-2.5 1.9-5.9 1.9-2.1 0-4.1-.7-6.1-2l-1.2-.8-1.2.8c-.8.6-8 5.9-8.2 18.7-.2 1.1 2.9 22.2 23.9 28.3 22.9-6.7 24.1-26.9 24-28.3z',
  SQUARE: 'M-24-48h48v48h-48z',
  SQUARE_ROUNDED: 'M24-8c0 4.4-3.6 8-8 8h-32c-4.4 0-8-3.6-8-8v-32c0-4.4 3.6-8 8-8h32c4.4 0 8 3.6 8 8v32z',
};

function getStringValue(...candidates: unknown[]): string {
  for (const candidate of candidates) {
    if (typeof candidate === 'string' && candidate.trim().length > 0) {
      return candidate.trim();
    }
  }

  return '';
}

function getBooleanValue(candidate: unknown, fallbackValue = false): boolean {
  if (typeof candidate === 'boolean') {
    return candidate;
  }

  if (typeof candidate === 'string') {
    const normalizedCandidate = candidate.trim().toLowerCase();

    if (normalizedCandidate === 'true' || normalizedCandidate === '1') {
      return true;
    }

    if (normalizedCandidate === 'false' || normalizedCandidate === '0') {
      return false;
    }
  }

  return fallbackValue;
}

export function resolveMapConfig(props: MapElementProps): ResolvedMapConfig {
  const config = props.mapConfig ?? {};

  return {
    mapProvider: getStringValue(config.MapProvider, config.mapProvider, props.mapProvider, 'leaflet')
      .toLowerCase(),
    tileUrl: getStringValue(config.TileUrl, config.tileUrl, props.leafletOsmUrl),
    styleUrl: getStringValue(config.StyleUrl, config.styleUrl, props.mapStyleUrl),
    accessToken: getStringValue(config.AccessToken, config.accessToken, props.mapAccessToken),
    attribution: getStringValue(config.Attribution, config.attribution, props.mapAttribution),
    isDepartmentOverride: getBooleanValue(
      config.IsDepartmentOverride ?? config.isDepartmentOverride,
      false,
    ),
  };
}

export function normalizeMapLayers(result: GetMapLayersResult | null): NormalizedMapLayer[] {
  const rawLayers = result?.Data?.Layers ?? [];

  return rawLayers
    .filter((layer) => layer.Data?.Features)
    .map((layer) => ({
      id: layer.Id,
      name: layer.Name,
      color: layer.Color,
      isOnByDefault: layer.IsOnByDefault,
      featureCollection: layer.Data?.Features as FeatureCollection,
    }));
}

function getMarkerTypeValue(marker: Pick<MapMarkerInfo, 'Type'>): number | null {
  if (typeof marker.Type === 'number') {
    return marker.Type;
  }

  if (typeof marker.Type === 'string') {
    const parsedType = Number.parseInt(marker.Type, 10);
    return Number.isNaN(parsedType) ? null : parsedType;
  }

  return null;
}

export function isPoiMarker(
  marker: Pick<MapMarkerInfo, 'Type' | 'PoiTypeId' | 'LayerId' | 'ImagePath'>,
): boolean {
  if (getMarkerTypeValue(marker) === mapMarkerTypes.poi) {
    return true;
  }

  if (typeof marker.PoiTypeId === 'number' && marker.PoiTypeId > 0) {
    return true;
  }

  if (typeof marker.LayerId === 'string' && marker.LayerId.startsWith('poi-type-')) {
    return true;
  }

  return typeof marker.ImagePath === 'string'
    && marker.ImagePath.trim().toLowerCase().startsWith('map-icon-');
}

export function getPoiLayerId(layer: Pick<PoiLayerInfo, 'PoiTypeId'> | Pick<MapMarkerInfo, 'PoiTypeId' | 'LayerId'>): string {
  if ('LayerId' in layer && typeof layer.LayerId === 'string' && layer.LayerId.length > 0) {
    return layer.LayerId;
  }

  return `poi-type-${layer.PoiTypeId ?? 0}`;
}

export function getPoiMarkerShapePath(markerShape?: string): string {
  const normalizedShape = typeof markerShape === 'string' && markerShape.trim().length > 0
    ? markerShape.trim().toUpperCase()
    : defaultPoiMarkerShape;

  return poiMarkerPaths[normalizedShape] ?? poiMarkerPaths[defaultPoiMarkerShape];
}

export function getLayerColor(layer: NormalizedMapLayer): string {
  const feature = layer.featureCollection.features[0];
  const properties = feature?.properties as GeoJsonProperties | null | undefined;

  if (typeof properties?.color === 'string' && properties.color.length > 0) {
    return properties.color;
  }

  return layer.color || '#2563eb';
}

function extractMapboxStyleId(stylePath: string): string {
  const normalizedPath = stylePath.trim().replace(/^\/+|\/+$/g, '');

  if (!normalizedPath) {
    return '';
  }

  const pathSegments = normalizedPath.split('/').filter((segment) => segment.length > 0);

  if (pathSegments.length < 2) {
    return '';
  }

  return `${pathSegments[0]}/${pathSegments[1]}`;
}

function getMapboxStyleId(styleUrl: string): string {
  const trimmedStyleUrl = styleUrl.trim();

  if (!trimmedStyleUrl) {
    return '';
  }

  const mapboxStylePrefix = 'mapbox://styles/';

  if (trimmedStyleUrl.toLowerCase().startsWith(mapboxStylePrefix)) {
    return extractMapboxStyleId(trimmedStyleUrl.substring(mapboxStylePrefix.length));
  }

  try {
    const mapboxStyleUri = new URL(trimmedStyleUrl);
    const path = mapboxStyleUri.pathname.replace(/^\/+|\/+$/g, '');
    const normalizedPath = path.toLowerCase();
    const stylesPrefix = 'styles/v1/';
    const stylesIndex = normalizedPath.indexOf(stylesPrefix);

    if (stylesIndex >= 0) {
      return extractMapboxStyleId(path.substring(stylesIndex + stylesPrefix.length));
    }
  } catch {
    return '';
  }

  return '';
}

export function isMapboxRendererEnabled(mapConfig: ResolvedMapConfig): boolean {
  return (
    mapConfig.mapProvider === 'mapbox' &&
    getMapboxStyleId(mapConfig.styleUrl).length > 0 &&
    mapConfig.accessToken.length > 0
  );
}

export function getMarkerIconUrl(marker: Pick<MapMarkerInfo, 'ImagePath'>): string {
  const imagePath = typeof marker.ImagePath === 'string' && marker.ImagePath.length > 0
    ? marker.ImagePath
    : 'pin';

  return `/images/Mapping/${imagePath}.png`;
}
