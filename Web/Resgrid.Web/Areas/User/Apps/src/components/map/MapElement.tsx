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
  isMapboxRendererEnabled,
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

export default function MapElement(props: MapElementProps) {
  const connectionRef = useRef<HubConnection | null>(null);

  const [mapData, setMapData] = useState<GetMapDataResult['Data'] | null>(null);
  const [layers, setLayers] = useState<MapRendererProps['layers']>([]);
  const [layerVisibility, setLayerVisibility] = useState<Record<string, boolean>>({});
  const [filterText, setFilterText] = useState('');
  const [showCalls, setShowCalls] = useState(true);
  const [showStations, setShowStations] = useState(true);
  const [showUnits, setShowUnits] = useState(true);
  const [showPersonnel, setShowPersonnel] = useState(true);
  const [hideLabels, setHideLabels] = useState(false);
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
        const matchesFilter =
          normalizedFilter.length === 0 || marker.Title.toLowerCase().includes(normalizedFilter);

        if (!matchesFilter) {
          return false;
        }

        switch (marker.Type) {
          case mapMarkerTypes.call:
            return showCalls;
          case mapMarkerTypes.unit:
            return showUnits;
          case mapMarkerTypes.station:
            return showStations;
          case mapMarkerTypes.personnel:
            return showPersonnel;
          default:
            return true;
        }
      });
  }, [filterText, mapData, markerPositionOverrides, showCalls, showPersonnel, showStations, showUnits]);

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
            result[layer.id] = layer.isOnByDefault;
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
            hideLabels={hideLabels}
            resolvedMapConfig={resolvedMapConfig}
            fitBoundsKey={fitBoundsKey}
          />
        )}

        {layers.length > 0 && (
          <div className="rg-map__layers rg-card">
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
