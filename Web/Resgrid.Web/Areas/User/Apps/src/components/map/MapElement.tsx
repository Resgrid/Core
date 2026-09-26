import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType } from 'react';
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
  type MapMarkerInfo,
  type MapRendererProps,
} from './mapTypes';
import './map.css';

export type { MapElementProps } from './mapTypes';

type MapRendererComponent = ComponentType<MapRendererProps>;

interface LivePosition {
  latitude: number;
  longitude: number;
  /** Fix time in ms, or null when the server did not send one. */
  timestamp: number | null;
  receivedAt: number;
}

// Coalesces reloads triggered by realtime traffic into one request.
const MARKER_RELOAD_DEBOUNCE_MS = 5000;
// A pushed marker the REST data does not contain (location older than the TTL, or hidden from this
// viewer by the visibility matrix) may trigger a reload at most this often.
const UNKNOWN_MARKER_RELOAD_INTERVAL_MS = 5 * 60 * 1000;
const CONNECT_RETRY_DELAYS_MS = [2000, 5000, 10000, 30000];

function getMarkerKey(markerId: string | number): string {
  return String(markerId).toLowerCase();
}

function parseTimestamp(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

function isValidCoordinate(latitude: number, longitude: number): boolean {
  return (
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    Math.abs(latitude) <= 90 &&
    Math.abs(longitude) <= 180 &&
    !(latitude === 0 && longitude === 0)
  );
}

// A REST snapshot can be older than a push that arrived while it was in flight, so only
// positions received after the fetch started survive a reload.
function keepPositionsReceivedSince(
  positions: Record<string, LivePosition>,
  since: number,
): Record<string, LivePosition> {
  return Object.fromEntries(
    Object.entries(positions).filter(([, position]) => position.receivedAt >= since),
  );
}

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
  const initialLoadCompleteRef = useRef(false);
  const knownMarkerKeysRef = useRef<Set<string>>(new Set());
  const unknownMarkerReloadsRef = useRef<Map<string, number>>(new Map());
  const reloadTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const reloadInFlightRef = useRef(false);

  const [mapData, setMapData] = useState<GetMapDataResult['Data'] | null>(null);
  // Kept apart from mapData so a background marker reload does not re-fit the camera.
  const [markerInfos, setMarkerInfos] = useState<MapMarkerInfo[]>([]);
  const [layers, setLayers] = useState<MapRendererProps['layers']>([]);
  const [layerVisibility, setLayerVisibility] = useState<Record<string, boolean>>({});
  const [filterText, setFilterText] = useState('');
  const [showCalls, setShowCalls] = useState(() => readBooleanPreference('rg-map:show-calls', true));
  const [showStations, setShowStations] = useState(() => readBooleanPreference('rg-map:show-stations', true));
  const [showUnits, setShowUnits] = useState(() => readBooleanPreference('rg-map:show-units', true));
  const [showPersonnel, setShowPersonnel] = useState(() => readBooleanPreference('rg-map:show-personnel', true));
  const [showHydrants, setShowHydrants] = useState(() => readBooleanPreference('rg-map:show-hydrants', true));
  const [showPois, setShowPois] = useState(() => readBooleanPreference('rg-map:show-pois', true));
  const [poiLayerVisibility, setPoiLayerVisibility] = useState<Record<string, boolean>>({});
  const [hideLabels, setHideLabels] = useState(() => readBooleanPreference('rg-map:hide-labels', false));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState(() => new Date().toString());
  const [markerPositionOverrides, setMarkerPositionOverrides] = useState<Record<string, LivePosition>>({});
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

    return markerInfos
      .map((marker) => {
        const positionOverride = markerPositionOverrides[getMarkerKey(marker.Id)];

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
          case mapMarkerTypes.hydrant:
            return showHydrants;
          case mapMarkerTypes.poi:
            return showPois && (poiLayerVisibility[getPoiLayerId(marker)] ?? true);
          default:
            return !isPoi || (showPois && (poiLayerVisibility[getPoiLayerId(marker)] ?? true));
        }
      });
  }, [
    filterText,
    markerInfos,
    markerPositionOverrides,
    poiLayerVisibility,
    showCalls,
    showPersonnel,
    showPois,
    showHydrants,
    showStations,
    showUnits,
  ]);

  useEffect(() => writeBooleanPreference('rg-map:show-calls', showCalls), [showCalls]);
  useEffect(() => writeBooleanPreference('rg-map:show-stations', showStations), [showStations]);
  useEffect(() => writeBooleanPreference('rg-map:show-units', showUnits), [showUnits]);
  useEffect(() => writeBooleanPreference('rg-map:show-personnel', showPersonnel), [showPersonnel]);
  useEffect(() => writeBooleanPreference('rg-map:show-pois', showPois), [showPois]);
  useEffect(() => writeBooleanPreference('rg-map:show-hydrants', showHydrants), [showHydrants]);
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

      const fetchStartedAt = Date.now();

      try {
        const [mapResponse, layerResponse] = await Promise.all([
          apiFetchJson<GetMapDataResult>('/api/v4/Mapping/GetMapDataAndMarkers'),
          apiFetchJson<GetMapLayersResult>('/api/v4/Mapping/GetMayLayers?type=0'),
        ]);

        if (cancelled) {
          return;
        }

        const markers = mapResponse.Data?.MapMakerInfos ?? [];
        knownMarkerKeysRef.current = new Set(markers.map((marker) => getMarkerKey(marker.Id)));
        setMarkerPositionOverrides((currentOverrides) => keepPositionsReceivedSince(currentOverrides, fetchStartedAt));
        setMapData(mapResponse.Data);
        setMarkerInfos(markers);
        initialLoadCompleteRef.current = true;

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

  // Background marker reload (after a reconnect, or for a pushed marker the map does not have yet).
  // Only the markers are replaced; map center/zoom stay put.
  const reloadMarkersAsync = useCallback(async () => {
    if (reloadInFlightRef.current) {
      return;
    }

    reloadInFlightRef.current = true;
    const fetchStartedAt = Date.now();

    try {
      const mapResponse = await apiFetchJson<GetMapDataResult>('/api/v4/Mapping/GetMapDataAndMarkers');
      const markers = mapResponse.Data?.MapMakerInfos ?? [];

      knownMarkerKeysRef.current = new Set(markers.map((marker) => getMarkerKey(marker.Id)));
      setMarkerInfos(markers);
      setMarkerPositionOverrides((currentOverrides) => keepPositionsReceivedSince(currentOverrides, fetchStartedAt));
      setLastUpdated(new Date().toString());
    } catch (reloadError) {
      console.error('Unable to reload map markers.', reloadError);
    } finally {
      reloadInFlightRef.current = false;
    }
  }, []);

  const requestMarkerReload = useCallback(
    (unknownMarkerKey?: string) => {
      if (!initialLoadCompleteRef.current) {
        return;
      }

      if (unknownMarkerKey) {
        const now = Date.now();
        const lastReload = unknownMarkerReloadsRef.current.get(unknownMarkerKey);

        if (lastReload !== undefined && now - lastReload < UNKNOWN_MARKER_RELOAD_INTERVAL_MS) {
          return;
        }

        unknownMarkerReloadsRef.current.set(unknownMarkerKey, now);
      }

      if (reloadTimerRef.current) {
        return;
      }

      const fireReload = () => {
        // A reload that started earlier cannot satisfy this request (e.g. a catch-up after reconnect).
        if (reloadInFlightRef.current) {
          reloadTimerRef.current = setTimeout(fireReload, MARKER_RELOAD_DEBOUNCE_MS);
          return;
        }

        reloadTimerRef.current = null;
        void reloadMarkersAsync();
      };

      reloadTimerRef.current = setTimeout(fireReload, MARKER_RELOAD_DEBOUNCE_MS);
    },
    [reloadMarkersAsync],
  );

  useEffect(() => {
    let disposed = false;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;

    // The push only moves a marker the REST data already returned: the server sends every
    // location in the department, while the REST data applies this viewer's visibility rules.
    const updateMarkerPosition = (
      markerId: string,
      latitude: unknown,
      longitude: unknown,
      timestamp: string | null | undefined,
    ) => {
      const nextLatitude = Number(latitude);
      const nextLongitude = Number(longitude);

      if (!isValidCoordinate(nextLatitude, nextLongitude)) {
        return;
      }

      const markerKey = getMarkerKey(markerId);
      const fixTime = parseTimestamp(timestamp);

      setMarkerPositionOverrides((currentOverrides) => {
        const existingOverride = currentOverrides[markerKey];

        // Trackers replay buffered fixes and queue consumers can reorder them; never move back.
        if (
          existingOverride &&
          fixTime !== null &&
          existingOverride.timestamp !== null &&
          fixTime < existingOverride.timestamp
        ) {
          return currentOverrides;
        }

        if (
          existingOverride &&
          existingOverride.latitude === nextLatitude &&
          existingOverride.longitude === nextLongitude &&
          existingOverride.timestamp === fixTime
        ) {
          return currentOverrides;
        }

        return {
          ...currentOverrides,
          [markerKey]: {
            latitude: nextLatitude,
            longitude: nextLongitude,
            timestamp: fixTime,
            receivedAt: Date.now(),
          },
        };
      });

      if (!knownMarkerKeysRef.current.has(markerKey)) {
        requestMarkerReload(markerKey);
      }

      setLastUpdated(new Date().toString());
    };

    const connectAsync = async (attempt: number) => {
      try {
        const connection = await connectGeolocationHub({
          onPersonnelLocationUpdated: (update: PersonnelLocationUpdate) => {
            if (update?.userId) {
              updateMarkerPosition(`p${update.userId}`, update.latitude, update.longitude, update.timestamp);
            }
          },
          onUnitLocationUpdated: (update: UnitLocationUpdate) => {
            if (update?.unitId !== undefined && update.unitId !== null && `${update.unitId}` !== '') {
              updateMarkerPosition(`u${update.unitId}`, update.latitude, update.longitude, update.timestamp);
            }
          },
          onResubscribed: () => requestMarkerReload(),
        });

        if (disposed) {
          await connection?.stop();
          return;
        }

        connectionRef.current = connection;

        // Positions sent before a late first connection were missed.
        if (attempt > 0) {
          requestMarkerReload();
        }
      } catch (connectionError) {
        console.error('Unable to connect to realtime geolocation updates.', connectionError);

        // Automatic reconnect only covers a connection that was once established.
        if (!disposed) {
          const delay = CONNECT_RETRY_DELAYS_MS[Math.min(attempt, CONNECT_RETRY_DELAYS_MS.length - 1)];
          retryTimer = setTimeout(() => {
            retryTimer = null;
            void connectAsync(attempt + 1);
          }, delay);
        }
      }
    };

    void connectAsync(0);

    return () => {
      disposed = true;

      if (retryTimer) {
        clearTimeout(retryTimer);
      }

      if (reloadTimerRef.current) {
        clearTimeout(reloadTimerRef.current);
        reloadTimerRef.current = null;
      }

      if (connectionRef.current) {
        void connectionRef.current.stop();
        connectionRef.current = null;
      }
    };
  }, [requestMarkerReload]);

  // Re-fit when the user changes what is shown, not when realtime traffic or a background reload
  // changes the marker set: that would pull the camera away from wherever the user has panned.
  const fitBoundsKey = useMemo(() => {
    const hiddenPoiLayerIds = Object.entries(poiLayerVisibility)
      .filter(([, isVisible]) => !isVisible)
      .map(([layerId]) => layerId)
      .sort()
      .join('|');

    return [
      mapData?.CenterLat ?? '',
      mapData?.CenterLon ?? '',
      mapData?.ZoomLevel ?? '',
      showCalls,
      showStations,
      showUnits,
      showPersonnel,
      showHydrants,
      showPois,
      hiddenPoiLayerIds,
      filterText.trim().toLowerCase(),
    ].join(':');
  }, [
    filterText,
    mapData?.CenterLat,
    mapData?.CenterLon,
    mapData?.ZoomLevel,
    poiLayerVisibility,
    showCalls,
    showHydrants,
    showPersonnel,
    showPois,
    showStations,
    showUnits,
  ]);

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
      {mapData?.HydrantsError && <div className="rg-error rg-map__message" role="alert">{mapData.HydrantsError}</div>}

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

        {(layers.length > 0 || poiLayers.length > 0 || mapData?.HydrantsAvailable) && (
          <div className="rg-map__layers rg-card">
            {mapData?.HydrantsAvailable && (
              <label className="rg-map__layer-toggle">
                <input type="checkbox" checked={showHydrants} onChange={(event) => setShowHydrants(event.target.checked)} />
                <span>{(window as any).rgHydrantMap?.label || 'Hydrants'}</span>
              </label>
            )}
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
