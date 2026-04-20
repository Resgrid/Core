import type { FeatureCollection, GeoJsonProperties } from 'geojson';

export const mapMarkerTypes = {
  call: 0,
  unit: 1,
  station: 2,
  personnel: 3,
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
  Type: number;
}

export interface GetMapDataResult {
  Data: {
    CenterLat: number;
    CenterLon: number;
    ZoomLevel: number;
    MapMakerInfos: MapMarkerInfo[];
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
  hideLabels: boolean;
  resolvedMapConfig: ResolvedMapConfig;
  fitBoundsKey: string;
}

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
