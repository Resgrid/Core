import { useCallback, useEffect, useMemo, useRef, useState, type ComponentType } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import LoadingIndicator from '../shared/LoadingIndicator';
import { apiFetchJson } from '../../runtime/api';
import { connectEventingHub, connectGeolocationHub } from '../../runtime/signalr';
import {
  isMapboxRendererEnabled,
  resolveMapConfig,
  type GetMapDataResult,
  type MapConfigPayload,
  type MapRendererProps,
} from '../map/mapTypes';
import {
  applyLivePosition,
  buildUnitMapView,
  isMissingLoadedMarker,
  isTrackedKey,
  isValidCoordinate,
  keepPositionsReceivedSince,
  parseTimestamp,
  personMarkerKey,
  unitMarkerKey,
  type GetUnitMapDataResult,
  type LivePosition,
  type UnitMapData,
} from './unitMapModel';
import '../map/map.css';
import './unitMap.css';

export interface UnitMapElementProps {
  unitId?: number;
  departmentId?: number;
  mapHeight?: string;
  strings?: Record<string, string> | null;
  leafletOsmUrl?: string;
  mapAttribution?: string;
  mapProvider?: string;
  mapStyleUrl?: string;
  mapAccessToken?: string;
  mapConfig?: MapConfigPayload | null;
  hostElement?: HTMLElement;
}

type MapRendererComponent = ComponentType<MapRendererProps>;

// The Razor page passes the Units resx values; these cover a key it does not send.
const defaultStrings: Record<string, string> = {
  UnitMapCrew: 'Crew',
  UnitMapUnitPosition: 'Unit position',
  UnitMapNoUnitPosition: 'This unit is not reporting a position, so the map is fitted to its crew.',
  UnitMapNoPositions: 'Neither this unit nor its crew has a current position.',
  UnitMapNoCrew: "No one is assigned to this unit's roles.",
  UnitMapNoLocation: 'No current location',
  UnitMapLocatedAt: 'Located {0}',
  UnitMapKeepInView: 'Keep crew in view',
  UnitMapFitMap: 'Fit map',
  UnitMapLastUpdate: 'Last update: {0}',
  UnitMapLoadError: 'Unable to load the unit map.',
  UnitMapNotFound: 'This unit could not be found, or you may not view it.',
};

// Coalesces reloads from a burst of crew events (a staffing save touches every seat) into one request.
const RELOAD_DEBOUNCE_MS = 1500;
// Status events carry no unit or user id, so in a busy department most are someone else's; they reload at most this
// often. Live movement arrives on the geolocation hub and needs no reload.
const STATUS_RELOAD_DEBOUNCE_MS = 10000;
// A tracked entity with no loaded position that starts reporting triggers a reload at most this often.
const MISSING_MARKER_RELOAD_INTERVAL_MS = 60 * 1000;
const CONNECT_RETRY_DELAYS_MS = [2000, 5000, 10000, 30000];
// As close as a fit may zoom: one member alone, or a crew standing together, fills the view without losing the street.
const FIT_MAX_ZOOM = 18;
const KEEP_IN_VIEW_PREFERENCE = 'rg-unit-map:keep-in-view';

function format(template: string, ...args: string[]): string {
  return template.replace(/\{(\d+)\}/g, (match, index: string) => args[Number(index)] ?? match);
}

function readKeepInView(): boolean {
  try {
    return window.localStorage.getItem(KEEP_IN_VIEW_PREFERENCE) !== 'false';
  } catch {
    return true;
  }
}

function writeKeepInView(value: boolean): void {
  try {
    window.localStorage.setItem(KEEP_IN_VIEW_PREFERENCE, value ? 'true' : 'false');
  } catch {
    // Storage unavailable; the choice lasts for this page only.
  }
}

/**
 * Connects with retries until it succeeds or the effect is disposed: automatic reconnect only covers a connection that
 * was once established. Returns the cleanup.
 */
function keepConnected(
  connect: () => Promise<HubConnection | null>,
  onConnected: (attempt: number) => void,
  label: string,
): () => void {
  let disposed = false;
  let connection: HubConnection | null = null;
  let retryTimer: ReturnType<typeof setTimeout> | null = null;

  const attemptAsync = async (attempt: number) => {
    try {
      const established = await connect();

      if (disposed) {
        await established?.stop();
        return;
      }

      connection = established;
      onConnected(attempt);
    } catch (connectionError) {
      console.error(`Unable to connect to realtime ${label}.`, connectionError);

      if (!disposed) {
        const delay = CONNECT_RETRY_DELAYS_MS[Math.min(attempt, CONNECT_RETRY_DELAYS_MS.length - 1)];
        retryTimer = setTimeout(() => {
          retryTimer = null;
          void attemptAsync(attempt + 1);
        }, delay);
      }
    }
  };

  void attemptAsync(0);

  return () => {
    disposed = true;

    if (retryTimer) {
      clearTimeout(retryTimer);
    }

    void connection?.stop();
    connection = null;
  };
}

export default function UnitMapElement(props: UnitMapElementProps) {
  const unitId = props.unitId ?? 0;
  const departmentId = props.departmentId ?? 0;
  const mapHeight = props.mapHeight || '600px';

  const dataRef = useRef<UnitMapData | null>(null);
  // The host re-parses the strings attribute on every render; read through a ref so a new object never re-runs the
  // loaders and hub connections that use the text.
  const stringsRef = useRef(props.strings);
  stringsRef.current = props.strings;
  const reloadTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const reloadDueRef = useRef<number | null>(null);
  const reloadInFlightRef = useRef(false);
  const reloadQueuedRef = useRef(false);
  const missingMarkerReloadsRef = useRef<Map<string, number>>(new Map());

  const [data, setData] = useState<UnitMapData | null>(null);
  const [livePositions, setLivePositions] = useState<Record<string, LivePosition>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [keepInView, setKeepInView] = useState(readKeepInView);
  const [refitCount, setRefitCount] = useState(0);
  const [lastUpdated, setLastUpdated] = useState(() => new Date().toLocaleString());
  const [MapRenderer, setMapRenderer] = useState<MapRendererComponent | null>(null);
  const [rendererError, setRendererError] = useState<string | null>(null);

  const t = useCallback(
    (key: string, ...args: string[]) => format(stringsRef.current?.[key] || defaultStrings[key] || key, ...args),
    [],
  );

  const resolvedMapConfig = useMemo(
    () => resolveMapConfig(props),
    [props.leafletOsmUrl, props.mapAccessToken, props.mapAttribution, props.mapConfig, props.mapProvider, props.mapStyleUrl],
  );
  const useMapboxRenderer = useMemo(() => isMapboxRendererEnabled(resolvedMapConfig), [resolvedMapConfig]);
  const hasMapSource = useMapboxRenderer || resolvedMapConfig.tileUrl.length > 0;

  useEffect(() => writeKeepInView(keepInView), [keepInView]);

  useEffect(() => {
    let cancelled = false;

    if (!hasMapSource) {
      return () => {
        cancelled = true;
      };
    }

    const loadRendererAsync = async () => {
      try {
        const rendererModule = useMapboxRenderer ? await import('../map/MapboxMapView') : await import('../map/LeafletMapView');

        if (!cancelled) {
          setMapRenderer(() => rendererModule.default);
        }
      } catch (rendererLoadError) {
        if (!cancelled) {
          setRendererError(rendererLoadError instanceof Error ? rendererLoadError.message : 'Unable to load the map renderer.');
        }
      }
    };

    void loadRendererAsync();

    return () => {
      cancelled = true;
    };
  }, [hasMapSource, useMapboxRenderer]);

  const loadAsync = useCallback(async () => {
    if (unitId <= 0) {
      setLoading(false);
      setError(t('UnitMapNotFound'));
      return;
    }

    // A reload asked for while one is running runs again afterwards, so the last event is never lost.
    if (reloadInFlightRef.current) {
      reloadQueuedRef.current = true;
      return;
    }

    reloadInFlightRef.current = true;
    const fetchStartedAt = Date.now();

    try {
      const response = await apiFetchJson<GetUnitMapDataResult>('/api/v4/Mapping/GetUnitMapData', undefined, { unitId });

      if (response.Status === 'not_found' || !response.Data) {
        dataRef.current = null;
        setData(null);
        setError(t('UnitMapNotFound'));
        return;
      }

      dataRef.current = response.Data;
      setData(response.Data);
      setLivePositions((current) => keepPositionsReceivedSince(current, fetchStartedAt));
      setError(null);
      setLastUpdated(new Date().toLocaleString());
    } catch (loadError) {
      console.error('Unable to load the unit map.', loadError);

      // A failed background reload keeps the map it already shows.
      if (!dataRef.current) {
        setError(t('UnitMapLoadError'));
      }
    } finally {
      reloadInFlightRef.current = false;
      setLoading(false);

      if (reloadQueuedRef.current) {
        reloadQueuedRef.current = false;
        void loadAsync();
      }
    }
  }, [t, unitId]);

  // A pending reload due sooner already covers this request; one due later is brought forward to it.
  const requestReload = useCallback(
    (delayMs: number = RELOAD_DEBOUNCE_MS) => {
      const due = Date.now() + delayMs;

      if (reloadTimerRef.current) {
        if (reloadDueRef.current !== null && reloadDueRef.current <= due) {
          return;
        }

        clearTimeout(reloadTimerRef.current);
      }

      reloadDueRef.current = due;
      reloadTimerRef.current = setTimeout(() => {
        reloadTimerRef.current = null;
        reloadDueRef.current = null;
        void loadAsync();
      }, delayMs);
    },
    [loadAsync],
  );

  useEffect(() => {
    void loadAsync();
  }, [loadAsync]);

  useEffect(
    () => () => {
      if (reloadTimerRef.current) {
        clearTimeout(reloadTimerRef.current);
        reloadTimerRef.current = null;
      }
    },
    [],
  );

  // Positions: the department subscription carries every location this viewer may see; only the unit's are kept.
  useEffect(() => {
    const applyFix = (key: string, latitude: unknown, longitude: unknown, timestamp: string | null | undefined) => {
      const nextLatitude = Number(latitude);
      const nextLongitude = Number(longitude);

      if (!isTrackedKey(dataRef.current, key) || !isValidCoordinate(nextLatitude, nextLongitude)) {
        return;
      }

      setLivePositions((current) => applyLivePosition(current, key, nextLatitude, nextLongitude, parseTimestamp(timestamp), Date.now()));
      setLastUpdated(new Date().toLocaleString());

      if (isMissingLoadedMarker(dataRef.current, key)) {
        const now = Date.now();
        const lastReload = missingMarkerReloadsRef.current.get(key);

        if (lastReload === undefined || now - lastReload >= MISSING_MARKER_RELOAD_INTERVAL_MS) {
          missingMarkerReloadsRef.current.set(key, now);
          requestReload();
        }
      }
    };

    return keepConnected(
      () =>
        connectGeolocationHub({
          onPersonnelLocationUpdated: (update) => {
            if (update?.userId) {
              applyFix(personMarkerKey(update.userId), update.latitude, update.longitude, update.timestamp);
            }
          },
          onUnitLocationUpdated: (update) => {
            if (update?.unitId !== undefined && update.unitId !== null && `${update.unitId}` !== '') {
              applyFix(unitMarkerKey(update.unitId), update.latitude, update.longitude, update.timestamp);
            }
          },
          onResubscribed: requestReload,
        }),
      // Fixes sent before a late first connection were missed.
      (attempt) => {
        if (attempt > 0) {
          requestReload();
        }
      },
      'geolocation updates',
    );
  }, [requestReload]);

  // Crew changes, and status changes that can carry a position, reload the unit's map.
  useEffect(() => {
    if (departmentId <= 0) {
      return undefined;
    }

    return keepConnected(
      () =>
        connectEventingHub(departmentId, {
          onUnitActiveRolesUpdated: (changedUnitId) => {
            if (changedUnitId === '0' || changedUnitId === String(unitId)) {
              requestReload();
            }
          },
          // These carry a state or log id rather than the unit or user, so any one of them may be ours: a status saved
          // with a location stands in for a missing or expired GPS fix.
          onUnitStatusUpdated: () => requestReload(STATUS_RELOAD_DEBOUNCE_MS),
          onPersonnelStatusUpdated: () => {
            if ((dataRef.current?.Crew.length ?? 0) > 0) {
              requestReload(STATUS_RELOAD_DEBOUNCE_MS);
            }
          },
          onResubscribed: () => requestReload(),
        }),
      (attempt) => {
        if (attempt > 0) {
          requestReload();
        }
      },
      'department events',
    );
  }, [departmentId, requestReload, unitId]);

  const view = useMemo(() => (data ? buildUnitMapView(data, livePositions) : null), [data, livePositions]);

  // Only the default view: the renderers fit the markers themselves and fall back to this when there are none.
  const mapData = useMemo<GetMapDataResult['Data'] | null>(
    () => (data ? { CenterLat: data.CenterLat, CenterLon: data.CenterLon, ZoomLevel: data.ZoomLevel, MapMakerInfos: [] } : null),
    [data?.CenterLat, data?.CenterLon, data?.ZoomLevel],
  );

  // Re-fit on "Fit map", and, while keeping the crew in view, when someone joins, leaves, or starts or stops reporting.
  // A position moving does not change the key: the renderer only re-fits when a marker leaves the view.
  const locatedKey = useMemo(
    () => (view ? view.markers.map((marker) => marker.Id.toLowerCase()).sort().join('|') : ''),
    [view],
  );
  const fitBoundsKey = keepInView ? `${refitCount}:${locatedKey}` : `${refitCount}`;

  const formatLocatedAt = (locatedAt: number | null) =>
    locatedAt === null ? t('UnitMapNoLocation') : t('UnitMapLocatedAt', new Date(locatedAt).toLocaleString());

  const hasUnitPosition = Boolean(view?.unit.marker);
  const locatedCrewCount = view ? view.crew.filter((entry) => entry.marker !== null).length : 0;

  return (
    <div className="rg-map rg-unit-map">
      <div className="rg-map__toolbar">
        <div className="rg-map__toolbar-section">
          <label className="rg-map__checkbox">
            <input type="checkbox" checked={keepInView} onChange={(event) => setKeepInView(event.target.checked)} />
            <span>{t('UnitMapKeepInView')}</span>
          </label>
        </div>
        <div className="rg-map__toolbar-section">
          <button type="button" className="btn btn-xs btn-default" onClick={() => setRefitCount((count) => count + 1)} disabled={!view}>
            {t('UnitMapFitMap')}
          </button>
        </div>
      </div>

      {(error ?? rendererError) && <div className="rg-error rg-map__message" role="alert">{error ?? rendererError}</div>}

      {view && !hasUnitPosition && locatedCrewCount > 0 && (
        <div className="rg-unit-map__notice" role="status">{t('UnitMapNoUnitPosition')}</div>
      )}
      {view && !hasUnitPosition && locatedCrewCount === 0 && (
        <div className="rg-unit-map__notice" role="status">{t('UnitMapNoPositions')}</div>
      )}

      <div className="rg-unit-map__layout">
        <div className="rg-map__viewport rg-unit-map__viewport" style={{ height: mapHeight }}>
          {MapRenderer && hasMapSource && (
            <MapRenderer
              mapData={mapData}
              markers={view?.markers ?? []}
              layers={[]}
              layerVisibility={{}}
              poiLayers={[]}
              poiLayerVisibility={{}}
              hideLabels={false}
              resolvedMapConfig={resolvedMapConfig}
              fitBoundsKey={fitBoundsKey}
              fitMaxZoom={FIT_MAX_ZOOM}
              keepMarkersInView={keepInView}
            />
          )}

          {(loading || (hasMapSource && !MapRenderer && !rendererError)) && (
            <div className="rg-map__overlay">
              <LoadingIndicator label="Loading map..." />
            </div>
          )}
        </div>

        {view && (
          <aside className="rg-unit-map__panel rg-card" aria-label={t('UnitMapCrew')}>
            <div className="rg-unit-map__panel-title">{t('UnitMapUnitPosition')}</div>
            <div className="rg-unit-map__row">
              <span className="rg-unit-map__name">{data?.Name}</span>
              <span className="rg-unit-map__detail">{formatLocatedAt(view.unit.locatedAt)}</span>
            </div>

            <div className="rg-unit-map__panel-title">{t('UnitMapCrew')}</div>
            {view.crew.length === 0 && <div className="rg-unit-map__detail">{t('UnitMapNoCrew')}</div>}
            <ul className="rg-unit-map__crew">
              {view.crew.map((entry) => (
                <li key={entry.key} className={`rg-unit-map__row${entry.marker ? '' : ' rg-unit-map__row--unlocated'}`}>
                  <span className="rg-unit-map__name">{entry.member.Name}</span>
                  {entry.member.RoleName && <span className="rg-unit-map__role">{entry.member.RoleName}</span>}
                  <span className="rg-unit-map__detail">{formatLocatedAt(entry.locatedAt)}</span>
                </li>
              ))}
            </ul>
          </aside>
        )}
      </div>

      <div className="rg-map__footer">
        <span>{t('UnitMapLastUpdate', lastUpdated)}</span>
      </div>
    </div>
  );
}
