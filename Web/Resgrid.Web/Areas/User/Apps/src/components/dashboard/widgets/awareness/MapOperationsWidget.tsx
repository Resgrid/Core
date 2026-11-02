import { useEffect, useMemo, useState } from 'react';
import { apiFetchJson } from '../../../../runtime/api';
import {
  getLayerColor,
  mapMarkerTypes,
  normalizeMapLayers,
  type GetMapDataResult,
  type GetMapLayersResult,
  type MapMarkerInfo,
  type NormalizedMapLayer,
} from '../../../map/mapTypes';
import { EmptyState, MiniMap, type MiniMapGeoJsonLayer, type MiniMapMarker } from '../../primitives';
import type { WidgetProps } from '../types';
import './awarenessWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Awareness/MapOperationsWidgetData.cs */
export interface MapOperationsWidgetData {
  MarkersUrl: string;
  LayersUrl: string;
  ShowCalls: boolean;
  ShowUnits: boolean;
  ShowPersonnel: boolean;
  ShowStations: boolean;
  ShowPois: boolean;
  ShowLayers: boolean;
  CenterLatitude?: number | null;
  CenterLongitude?: number | null;
  ZoomLevel: number;
  ZoomFromSettings: boolean;
  /** The department's night base map, when its night style differs from the day one (null otherwise). */
  NightMap?: { MapProvider?: string | null; TileUrl?: string | null; StyleUrl?: string | null; AccessToken?: string | null; Attribution?: string | null } | null;
  /** Pin color per map icon style ("responding", "high", "unknown"…). */
  StyleColors?: Record<string, string> | null;
}

type LayerKey = 'calls' | 'units' | 'personnel' | 'stations' | 'pois';

const LAYER_ORDER: LayerKey[] = ['calls', 'units', 'personnel', 'stations', 'pois'];

function layerOf(marker: MapMarkerInfo): LayerKey | null {
  const type = typeof marker.Type === 'number' ? marker.Type : Number.parseInt(String(marker.Type), 10);
  switch (type) {
    case mapMarkerTypes.call:
      return 'calls';
    case mapMarkerTypes.unit:
      return 'units';
    case mapMarkerTypes.personnel:
      return 'personnel';
    case mapMarkerTypes.station:
      return 'stations';
    case mapMarkerTypes.poi:
    case mapMarkerTypes.hydrant:
      return 'pois';
    default:
      return null;
  }
}

/** The Mapping page's own per-viewer layer choice (rg-map stores it under this key), else the layer's default. */
function storedLayerVisibility(layerId: string, fallback: boolean): boolean {
  try {
    const stored = window.localStorage.getItem(`rg-map:layer:${layerId}`);
    return stored === null ? fallback : stored === 'true';
  } catch {
    return fallback;
  }
}

/**
 * The department's night style applies the way the apps apply it (Resgrid's only day/night switch): when the device
 * asks for a dark color scheme. The website itself has no dark theme, so a light-scheme browser always gets the day map.
 */
function usePrefersDark(): boolean {
  const query = useMemo(() => (typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : null), []);
  const [dark, setDark] = useState(() => query?.matches ?? false);
  useEffect(() => {
    if (!query) return undefined;
    const onChange = (event: MediaQueryListEvent) => setDark(event.matches);
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, [query]);
  return dark;
}

/**
 * map.operations (plan §7.4). Markers and department layers come from the endpoints the rg-map element uses (dispatch
 * scope, visibility matrix and icon styles are applied there); the widget's own data only carries the member's layer
 * choices, the department center/zoom, the style colors and the night base map. The provider data refetches on
 * calls/unit/personnel events, and every refetch reloads the markers (layers are configuration and load once).
 */
export default function MapOperationsWidget({ data, t }: WidgetProps<MapOperationsWidgetData>) {
  const [markers, setMarkers] = useState<MapMarkerInfo[] | null>(null);
  const [center, setCenter] = useState<{ lat: number; lng: number; zoom: number } | null>(null);
  const [failed, setFailed] = useState(false);
  const [layers, setLayers] = useState<NormalizedMapLayer[]>([]);
  const [layerToggles, setLayerToggles] = useState<Record<string, boolean>>({});
  const [hidden, setHidden] = useState<LayerKey[]>([]);
  const dark = usePrefersDark();

  const enabled = useMemo(() => {
    const set = new Set<LayerKey>();
    if (data.ShowCalls) set.add('calls');
    if (data.ShowUnits) set.add('units');
    if (data.ShowPersonnel) set.add('personnel');
    if (data.ShowStations) set.add('stations');
    if (data.ShowPois) set.add('pois');
    return set;
  }, [data.ShowCalls, data.ShowUnits, data.ShowPersonnel, data.ShowStations, data.ShowPois]);

  useEffect(() => {
    let cancelled = false;
    apiFetchJson<GetMapDataResult>(data.MarkersUrl)
      .then((result) => {
        if (cancelled) return;
        setMarkers(result?.Data?.MapMakerInfos ?? []);
        if (result?.Data) setCenter({ lat: result.Data.CenterLat, lng: result.Data.CenterLon, zoom: result.Data.ZoomLevel });
        setFailed(false);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
    };
    // Each provider refetch (a new data object) reloads the markers.
  }, [data]);

  useEffect(() => {
    if (!data.ShowLayers || !data.LayersUrl) {
      setLayers([]);
      return undefined;
    }
    let cancelled = false;
    apiFetchJson<GetMapLayersResult>(data.LayersUrl)
      .then((result) => {
        if (!cancelled) setLayers(normalizeMapLayers(result));
      })
      .catch(() => {
        // Layers are an extra; the map still shows its markers without them.
        if (!cancelled) setLayers([]);
      });
    return () => {
      cancelled = true;
    };
  }, [data.ShowLayers, data.LayersUrl]);

  const colors = useMemo(() => data.StyleColors ?? {}, [data.StyleColors]);
  const mapped = useMemo<MiniMapMarker[]>(() => {
    const result: MiniMapMarker[] = [];
    for (const marker of markers ?? []) {
      const layer = layerOf(marker);
      if (!layer || !enabled.has(layer) || !Number.isFinite(marker.Latitude) || !Number.isFinite(marker.Longitude) || (marker.Latitude === 0 && marker.Longitude === 0)) {
        continue;
      }

      const style = marker.IconStyle ?? undefined;
      result.push({
        id: `${layer}:${marker.Id}`,
        lat: marker.Latitude,
        lng: marker.Longitude,
        layer,
        kind: layer === 'stations' ? 'station' : 'dot',
        radius: layer === 'calls' ? 8 : layer === 'pois' ? 5 : 6,
        color: layer === 'pois' ? marker.Color || undefined : (style && colors[style]) || colors.unknown || undefined,
        tooltip: marker.Title,
      });
    }
    return result;
  }, [markers, enabled, colors]);

  const geoJsonLayers = useMemo<MiniMapGeoJsonLayer[]>(
    () =>
      layers.map((layer) => ({
        id: layer.id,
        data: layer.featureCollection,
        color: getLayerColor(layer),
        visible: layerToggles[layer.id] ?? storedLayerVisibility(layer.id, layer.isOnByDefault),
      })),
    [layers, layerToggles],
  );

  const lat = data.CenterLatitude ?? center?.lat;
  const lng = data.CenterLongitude ?? center?.lng;
  const zoom = data.ZoomFromSettings ? data.ZoomLevel : center?.zoom ?? data.ZoomLevel;
  const mapCenter = useMemo<[number, number] | undefined>(() => (lat !== undefined && lng !== undefined ? [lat, lng] : undefined), [lat, lng]);
  const night = dark && data.NightMap?.TileUrl ? data.NightMap : null;

  if (failed && !markers) {
    return <EmptyState icon="fa-map-o" text={t('W.map.operations.LoadError')} />;
  }

  // The map is created once the first marker load answers, so a fixed zoom (no fit) opens on the department center.
  if (!markers) {
    return <div className="rg-minimap rg-minimap-loading" role="region" aria-label={t('W.map.operations.Title')} aria-busy="true" />;
  }

  const present = LAYER_ORDER.filter((layer) => mapped.some((marker) => marker.layer === layer));

  return (
    <MiniMap
      markers={mapped}
      geoJsonLayers={geoJsonLayers}
      tileUrl={night?.TileUrl ?? undefined}
      dark={dark}
      attribution={night ? night.Attribution ?? undefined : undefined}
      center={mapCenter}
      zoom={zoom}
      fit={!data.ZoomFromSettings}
      hiddenLayers={hidden}
      ariaLabel={t('W.map.operations.Title')}
      legend={
        present.length > 0 || geoJsonLayers.length > 0 ? (
          <>
            {present.map((layer) => (
              <label key={layer} className="rg-mapops-layer">
                <input
                  type="checkbox"
                  checked={!hidden.includes(layer)}
                  onChange={(event) => setHidden((current) => (event.target.checked ? current.filter((x) => x !== layer) : [...current, layer]))}
                />
                {t(`W.map.operations.Layer.${layer}`)}
              </label>
            ))}
            {geoJsonLayers.map((layer, index) => (
              <label key={`geo:${layer.id}`} className="rg-mapops-layer">
                <input
                  type="checkbox"
                  checked={layer.visible !== false}
                  onChange={(event) => setLayerToggles((current) => ({ ...current, [layer.id]: event.target.checked }))}
                />
                <i className="rg-mapops-swatch" style={{ background: layer.color }} aria-hidden="true" />
                {layers[index]?.name || t('W.map.operations.Layer.layer')}
              </label>
            ))}
          </>
        ) : undefined
      }
    />
  );
}
