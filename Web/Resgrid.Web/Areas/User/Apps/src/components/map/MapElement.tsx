import { useEffect, useMemo, useRef, useState, type ComponentType } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import LoadingIndicator from '../shared/LoadingIndicator';
import { apiFetchJson } from '../../runtime/api';
import {
  connectGeolocationHub,
  type PersonnelLocationUpdate,
  type UnitLocationUpdate,
} from '../../runtime/signalr';
import {
  getPoiLayerId,
  isMapboxRendererEnabled,
  isPoiMarker,
  mapMarkerTypes,
  normalizeMapLayers,
  resolveMapConfig,
  type GetMapDataResult,
  type GetMapLayersResult,
  type MapElementProps,
  type MapRendererProps,
} from './mapTypes';
import './map.css';

export type { MapElementProps } from './mapTypes';

type MapRendererComponent = ComponentType<MapRendererProps>;

function getErrorMessage(error: unknown, fallbackMessage: string): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message;
  }

  return fallbackMessage;
}

function readBooleanPreference(storageKey: string, fallbackValue: boolean): boolean {
  if (typeof window === 'undefined') {
    return fallbackValue;
  }

  try {
    const storedValue = window.localStorage.getItem(storageKey);

    if (storedValue === null) {
      return fallbackValue;
    }

    return storedValue === 'true';
  } catch {
    return fallbackValue;
  }
}

function writeBooleanPreference(storageKey: string, value: boolean): void {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.setItem(storageKey, value ? 'true' : 'false');
  } catch {
    // Ignore storage failures and fall back to in-memory state.
  }
}

export default function MapElement(props: MapElementProps) {
  const connectionRef = useRef<HubConnection | null>(null);

  const [mapData, setMapData] = useState<GetMapDataResult['Data'] | null>(null);
  const [layers, setLayers] = useState<MapRendererProps['layers']>([]);
  const [layerVisibility, setLayerVisibility] = useState<Record<string, boolean>>({});
  const [filterText, setFilterText] = useState('');
  const [showCalls, setShowCalls] = useState(() => readBooleanPreference('rg-map:show-calls', true));
  const [showStations, setShowStations] = useState(() => readBooleanPreference('rg-map:show-stations', true));
  const [showUnits, setShowUnits] = useState(() => readBooleanPreference('rg-map:show-units', true));
  const [showPersonnel, setShowPersonnel] = useState(() => readBooleanPreference('rg-map:show-personnel', true));
  const [showPois, setShowPois] = useState(() => readBooleanPreference('rg-map:show-pois', true));
  const [poiLayerVisibility, setPoiLayerVisibility] = useState<Record<string, boolean>>({});
  const [hideLabels, setHideLabels] = useState(() => readBooleanPreference('rg-map:hide-labels', false));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState(() => new Date().toString());
  const [markerPositionOverrides, setMarkerPositionOverrides] = useState<
    Record<string, { latitude: number; longitude: number }>
  >({});
  const [MapRenderer, setMapRenderer] = useState<MapRendererComponent | null>(null);
  const [rendererLoading, setRendererLoading] = useState(true);
  const [rendererError, setRendererError] = useState<string | null>(null);

  const resolvedMapConfig = useMemo(
    () => resolveMapConfig(props),
    [
      props.leafletOsmUrl,
      props.mapAccessToken,
      props.mapAttribution,
      props.mapConfig,
      props.mapProvider,
      props.mapStyleUrl,
    ],
  );
  const mapHeight = props.mapHeight || '600px';
  const showButtons = props.showButtons ?? false;
  const useMapboxRenderer = useMemo(
    () => isMapboxRendererEnabled(resolvedMapConfig),
    [resolvedMapConfig],
  );
  const hasMapSource = useMapboxRenderer || resolvedMapConfig.tileUrl.length > 0;
  const poiLayers = useMemo(() => mapData?.PoiLayers ?? [], [mapData]);

  const visibleMarkers = useMemo(() => {
    const normalizedFilter = filterText.trim().toLowerCase();

    return (mapData?.MapMakerInfos ?? [])
      .map((marker) => {
        const positionOverride = markerPositionOverrides[marker.Id];

        if (!positionOverride) {
          return marker;
        }

        return {
          ...marker,
          Latitude: positionOverride.latitude,
          Longitude: positionOverride.longitude,
        };
      })
      .filter((marker) => {
        const markerTitle = typeof marker.Title === 'string' ? marker.Title.toLowerCase() : '';
        const markerType = typeof marker.Type === 'string' ? Number.parseInt(marker.Type, 10) : marker.Type;
        const isPoi = isPoiMarker(marker);
        const matchesFilter =
          normalizedFilter.length === 0 || markerTitle.includes(normalizedFilter);

        if (!matchesFilter) {
          return false;
        }

        switch (markerType) {
          case mapMarkerTypes.call:
            return showCalls;
          case mapMarkerTypes.unit:
            return showUnits;
          case mapMarkerTypes.station:
            return showStations;
          case mapMarkerTypes.personnel:
            return showPersonnel;
          case mapMarkerTypes.poi:
            return showPois && (poiLayerVisibility[getPoiLayerId(marker)] ?? true);
          default:
            return !isPoi || (showPois && (poiLayerVisibility[getPoiLayerId(marker)] ?? true));
        }
      });
  }, [
    filterText,
    mapData,
    markerPositionOverrides,
    poiLayerVisibility,
    showCalls,
    showPersonnel,
    showPois,
    showStations,
    showUnits,
  ]);

  useEffect(() => writeBooleanPreference('rg-map:show-calls', showCalls), [showCalls]);
  useEffect(() => writeBooleanPreference('rg-map:show-stations', showStations), [showStations]);
  useEffect(() => writeBooleanPreference('rg-map:show-units', showUnits), [showUnits]);
  useEffect(() => writeBooleanPreference('rg-map:show-personnel', showPersonnel), [showPersonnel]);
  useEffect(() => writeBooleanPreference('rg-map:show-pois', showPois), [showPois]);
  useEffect(() => writeBooleanPreference('rg-map:hide-labels', hideLabels), [hideLabels]);

  useEffect(() => {
    Object.entries(layerVisibility).forEach(([layerId, isVisible]) => {
      writeBooleanPreference(`rg-map:layer:${layerId}`, isVisible);
    });
  }, [layerVisibility]);

  useEffect(() => {
    Object.entries(poiLayerVisibility).forEach(([layerId, isVisible]) => {
      writeBooleanPreference(`rg-map:poi-layer:${layerId}`, isVisible);
    });
  }, [poiLayerVisibility]);

  useEffect(() => {
    let cancelled = false;

    setRendererLoading(true);
    setRendererError(null);
    setMapRenderer(null);

    if (!hasMapSource) {
      setRendererLoading(false);
      return () => {
        cancelled = true;
      };
    }

    const loadRendererAsync = async () => {
      try {
        const rendererModule = useMapboxRenderer
          ? await import('./MapboxMapView')
          : await import('./LeafletMapView');

        if (!cancelled) {
          setMapRenderer(() => rendererModule.default);
        }
      } catch (rendererLoadError) {
        if (!cancelled) {
          setRendererError(getErrorMessage(rendererLoadError, 'Unable to load the map renderer.'));
        }
      } finally {
        if (!cancelled) {
          setRendererLoading(false);
        }
      }
    };

    void loadRendererAsync();

    return () => {
      cancelled = true;
    };
  }, [hasMapSource, useMapboxRenderer]);

  useEffect(() => {
    let cancelled = false;

    const loadMapAsync = async () => {
      setLoading(true);
      setError(null);

      try {
        const [mapResponse, layerResponse] = await Promise.all([
          apiFetchJson<GetMapDataResult>('/api/v4/Mapping/GetMapDataAndMarkers'),
          apiFetchJson<GetMapLayersResult>('/api/v4/Mapping/GetMayLayers?type=0'),
        ]);

        if (cancelled) {
          return;
        }

        setMarkerPositionOverrides({});
        setMapData(mapResponse.Data);

        const normalizedLayers = normalizeMapLayers(layerResponse);
        setLayers(normalizedLayers);
        setLayerVisibility(
          normalizedLayers.reduce<Record<string, boolean>>((result, layer) => {
            result[layer.id] = readBooleanPreference(`rg-map:layer:${layer.id}`, layer.isOnByDefault);
            return result;
          }, {}),
        );

        const normalizedPoiLayers = mapResponse.Data?.PoiLayers ?? [];
        setPoiLayerVisibility(
          normalizedPoiLayers.reduce<Record<string, boolean>>((result, poiLayer) => {
            const layerId = getPoiLayerId(poiLayer);
            result[layerId] = readBooleanPreference(`rg-map:poi-layer:${layerId}`, true);
            return result;
          }, {}),
        );
        setLastUpdated(new Date().toString());
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : 'Unable to load map data.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void loadMapAsync();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let disposed = false;

    const updateMarkerPosition = (id: string, latitude: number, longitude: number) => {
      setMarkerPositionOverrides((currentOverrides) => {
        const existingOverride = currentOverrides[id];

        if (
          existingOverride &&
          existingOverride.latitude === latitude &&
          existingOverride.longitude === longitude
        ) {
          return currentOverrides;
        }

        return {
          ...currentOverrides,
          [id]: {
            latitude,
            longitude,
          },
        };
      });

      setLastUpdated(new Date().toString());
    };

    const connectAsync = async () => {
      try {
        const connection = await connectGeolocationHub({
          onPersonnelLocationUpdated: (update: PersonnelLocationUpdate) => {
            updateMarkerPosition(`p${update.userId}`, update.latitude, update.longitude);
          },
          onUnitLocationUpdated: (update: UnitLocationUpdate) => {
            updateMarkerPosition(`u${update.unitId}`, update.latitude, update.longitude);
          },
        });

        if (disposed) {
          await connection?.stop();
          return;
        }

        connectionRef.current = connection;
      } catch (connectionError) {
        console.error('Unable to connect to realtime geolocation updates.', connectionError);
      }
    };

    void connectAsync();

    return () => {
      disposed = true;

      if (connectionRef.current) {
        void connectionRef.current.stop();
        connectionRef.current = null;
      }
    };
  }, []);

  const fitBoundsKey = useMemo(() => {
    const visibleMarkerIds = visibleMarkers
      .map((marker) => marker.Id)
      .sort()
      .join('|');

    return `${mapData?.CenterLat ?? ''}:${mapData?.CenterLon ?? ''}:${mapData?.ZoomLevel ?? ''}:${visibleMarkerIds}`;
  }, [mapData?.CenterLat, mapData?.CenterLon, mapData?.ZoomLevel, visibleMarkers]);

  const missingSourceMessage = useMemo(() => {
    if (resolvedMapConfig.mapProvider === 'mapbox') {
      return 'Mapbox mode requires a style URL and access token.';
    }

    return 'Map configuration is missing a tile source. Pass `leafletosmurl` or `mapconfig`.';
  }, [resolvedMapConfig.mapProvider]);

  const combinedError = error ?? rendererError;

  const handleShowPoisChanged = (checked: boolean) => {
    setShowPois(checked);

    if (!checked) {
      return;
    }

    setPoiLayerVisibility((currentVisibility) => {
      const hasVisiblePoiLayer = poiLayers.some(
        (poiLayer) => currentVisibility[getPoiLayerId(poiLayer)] ?? true,
      );

      if (hasVisiblePoiLayer) {
        return currentVisibility;
      }

      return poiLayers.reduce<Record<string, boolean>>((nextVisibility, poiLayer) => {
        nextVisibility[getPoiLayerId(poiLayer)] = true;
        return nextVisibility;
      }, { ...currentVisibility });
    });
  };

  return (
    <div className="rg-map">
      {showButtons && (
        <div className="rg-map__toolbar">
          <div className="rg-map__toolbar-section">
            <input
              className="rg-map__search"
              type="text"
              placeholder="Filter marker text"
              value={filterText}
              onChange={(event) => setFilterText(event.target.value)}
            />

            <label className="rg-map__checkbox">
              <input
                type="checkbox"
                checked={hideLabels}
                onChange={(event) => setHideLabels(event.target.checked)}
              />
              <span>Hide labels</span>
            </label>
          </div>

          <div className="rg-map__toolbar-section">
            <label className="rg-map__checkbox">
              <input
                type="checkbox"
                checked={showCalls}
                onChange={(event) => setShowCalls(event.target.checked)}
              />
              <span>Show calls</span>
            </label>

            <label className="rg-map__checkbox">
              <input
                type="checkbox"
                checked={showStations}
                onChange={(event) => setShowStations(event.target.checked)}
              />
              <span>Show stations</span>
            </label>

            <label className="rg-map__checkbox">
              <input
                type="checkbox"
                checked={showUnits}
                onChange={(event) => setShowUnits(event.target.checked)}
              />
              <span>Show units</span>
            </label>

            <label className="rg-map__checkbox">
              <input
                type="checkbox"
                checked={showPersonnel}
                onChange={(event) => setShowPersonnel(event.target.checked)}
              />
              <span>Show personnel</span>
            </label>

            <label className="rg-map__checkbox">
              <input
                type="checkbox"
                checked={showPois}
                onChange={(event) => handleShowPoisChanged(event.target.checked)}
              />
              <span>Show POIs</span>
            </label>
          </div>
        </div>
      )}

      {!hasMapSource && <div className="rg-error">{missingSourceMessage}</div>}

      {combinedError && <div className="rg-error rg-map__message">{combinedError}</div>}

      <div className="rg-map__viewport" style={{ height: mapHeight }}>
        {MapRenderer && hasMapSource && (
          <MapRenderer
            mapData={mapData}
            markers={visibleMarkers}
            layers={layers}
            layerVisibility={layerVisibility}
            poiLayers={poiLayers}
            poiLayerVisibility={poiLayerVisibility}
            hideLabels={hideLabels}
            resolvedMapConfig={resolvedMapConfig}
            fitBoundsKey={fitBoundsKey}
          />
        )}

        {(layers.length > 0 || poiLayers.length > 0) && (
          <div className="rg-map__layers rg-card">
            {layers.length > 0 && (
              <div className="rg-map__layer-section">
                <div className="rg-map__layers-title">Map layers</div>
                <div className="rg-map__layer-list">
                  {layers.map((layer) => (
                    <label key={layer.id} className="rg-map__layer-toggle">
                      <input
                        type="checkbox"
                        checked={layerVisibility[layer.id] ?? false}
                        onChange={(event) =>
                          setLayerVisibility((currentVisibility) => ({
                            ...currentVisibility,
                            [layer.id]: event.target.checked,
                          }))
                        }
                      />
                      <span>{layer.name}</span>
                    </label>
                  ))}
                </div>
              </div>
            )}

            {poiLayers.length > 0 && (
              <div className="rg-map__layer-section">
                <div className="rg-map__layers-title">POI layers</div>
                <div className="rg-map__layer-list">
                  {poiLayers.map((poiLayer) => {
                    const layerId = getPoiLayerId(poiLayer);

                    return (
                      <label key={layerId} className="rg-map__layer-toggle">
                        <input
                          type="checkbox"
                          checked={poiLayerVisibility[layerId] ?? true}
                          onChange={(event) =>
                            setPoiLayerVisibility((currentVisibility) => ({
                              ...currentVisibility,
                              [layerId]: event.target.checked,
                            }))
                          }
                        />
                        <span
                          className="rg-map__layer-swatch"
                          style={{ backgroundColor: poiLayer.Color || '#2563eb' }}
                        />
                        <span>
                          {poiLayer.Name}
                          {poiLayer.IsDestination ? ' (Destination)' : ''}
                        </span>
                      </label>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        )}

        {(loading || rendererLoading) && (
          <div className="rg-map__overlay">
            <LoadingIndicator label="Loading map..." />
          </div>
        )}
      </div>

      <div className="rg-map__footer">
        <span>Last update: {lastUpdated}</span>
      </div>
    </div>
  );
}
