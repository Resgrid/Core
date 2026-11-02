import { lazy, Suspense, useEffect, useMemo, useRef, type ReactNode } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import type { GeoJsonObject } from 'geojson';
import { usePrimitiveRuntime } from '../runtime';

// A small Leaflet map filling the widget body, on the department's tiles (bootstrap Map config) unless the widget
// overrides them. Zoom, fade and marker animations are off: a hidden tab or preview pane stalls requestAnimationFrame,
// and an animated Leaflet never finishes its zoom there. Scroll-wheel zoom is off so the page scrolls past the map.

export interface MiniMapMarker {
  id: string;
  lat: number;
  lng: number;
  /** Fill color from the DTO (call priority, unit state). */
  color?: string;
  /** Circle radius in px (default 7; "station" markers ignore it). */
  radius?: number;
  /** Plain text tooltip (never HTML). */
  tooltip?: string;
  kind?: 'dot' | 'station';
  /** Layer key for the legend toggles. */
  layer?: string;
}

export interface MiniMapGeoJsonLayer {
  id: string;
  data: GeoJsonObject;
  /** Line, outline and point color (polygons are filled with it at low opacity). Default the primary blue. */
  color?: string;
  /** False hides the layer (default true). */
  visible?: boolean;
}

export interface MiniMapProps {
  markers: MiniMapMarker[];
  /** Drawn under the markers: lines/polygons styled from `color`, points as small circles. */
  geoJsonLayers?: MiniMapGeoJsonLayer[];
  /** Tile URL template overriding the department's bootstrap tiles (e.g. the department night style). */
  tileUrl?: string;
  /** Attribution for `tileUrl`. */
  attribution?: string;
  /** Used when there is nothing to fit. */
  center?: [number, number];
  zoom?: number;
  /** Fit the view to the markers (or, with no markers, the GeoJSON layers) when their ids change (default true). */
  fit?: boolean;
  /** Layer keys to hide (driven by the widget's legend checkboxes). */
  hiddenLayers?: readonly string[];
  ariaLabel: string;
  /** Rendered over the map, bottom-start (layer toggles, a key). */
  legend?: ReactNode;
  /** Use the department's night style (MapLibre maps; Leaflet maps take a night tileUrl instead). */
  dark?: boolean;
}

// Departments on Maps.MapLibreWeb get the MapLibre mini map (maplibre plan section 22.2).
const MiniMapLibre = lazy(() => import('./MiniMapLibre'));

export default function MiniMapView(props: MiniMapProps) {
  const { map: config } = usePrimitiveRuntime();
  const styleUrl = config?.Renderer === 'maplibre' ? (props.dark && config.MapLibreStyleUrlNight) || config.MapLibreStyleUrl : null;

  if (styleUrl) {
    return (
      <Suspense fallback={<div className="rg-minimap rg-minimap-loading" role="region" aria-label={props.ariaLabel} aria-busy="true" />}>
        <MiniMapLibre {...props} styleUrl={styleUrl} />
      </Suspense>
    );
  }

  return <LeafletMiniMap {...props} />;
}

const DEFAULT_TILES = 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png';
const DEFAULT_ATTRIBUTION = '&copy; OpenStreetMap contributors';
const DEFAULT_COLOR = '#228BCB';
const GEO_PANE = 'rgMiniMapGeo';

type FitTarget = { bounds: L.LatLngBounds } | { center: [number, number]; zoom: number };

function tooltipNode(text: string): HTMLElement {
  const node = document.createElement('span');
  node.textContent = text;
  return node;
}

function stationIcon(): L.DivIcon {
  const html = document.createElement('span');
  html.className = 'rg-minimap-station';
  const icon = document.createElement('i');
  icon.className = 'fa fa-home';
  html.appendChild(icon);
  return L.divIcon({ className: '', html, iconSize: [22, 22], iconAnchor: [11, 11] });
}

function hasSize(container: HTMLElement | null): container is HTMLElement {
  return !!container && container.isConnected && container.clientWidth > 0 && container.clientHeight > 0;
}

function LeafletMiniMap(props: MiniMapProps) {
  const { markers, geoJsonLayers, center, zoom = 13, fit = true, hiddenLayers, ariaLabel, legend } = props;
  const { map: config } = usePrimitiveRuntime();
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  const tileRef = useRef<L.TileLayer | null>(null);
  const markerGroupRef = useRef<L.LayerGroup | null>(null);
  const geoGroupRef = useRef<L.FeatureGroup | null>(null);
  const fittedKey = useRef<string | null>(null);
  // The view the last auto-fit asked for; a resize re-applies it unless the user has panned or zoomed since.
  const fitTarget = useRef<FitTarget | null>(null);
  const userMoved = useRef(false);
  const autoMoving = useRef(false);

  const tileUrl = props.tileUrl || config?.TileUrl || DEFAULT_TILES;
  const attribution = props.tileUrl ? props.attribution ?? '' : config?.Attribution || DEFAULT_ATTRIBUTION;

  // Applies the stored fit without animation; skipped while the container has no size (a fit then would zoom out to
  // the whole world) — the resize observer applies it once the container is laid out.
  const applyFit = useRef(() => {
    const map = mapRef.current;
    const target = fitTarget.current;
    if (!map || !target || !hasSize(containerRef.current)) {
      return;
    }
    autoMoving.current = true;
    try {
      if ('bounds' in target) {
        map.fitBounds(target.bounds, { maxZoom: 15, animate: false });
      } else {
        map.setView(target.center, target.zoom, { animate: false });
      }
    } finally {
      autoMoving.current = false;
    }
  });

  // The map itself: created once, removed on unmount.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return undefined;
    }
    const map = L.map(container, {
      zoomAnimation: false,
      fadeAnimation: false,
      markerZoomAnimation: false,
      inertia: false,
      scrollWheelZoom: false,
      zoomControl: true,
    }).setView(center ?? [39.5, -98.35], center ? zoom : 4);
    // GeoJSON draws in its own pane under the markers' overlay pane (400).
    map.createPane(GEO_PANE).style.zIndex = '350';
    geoGroupRef.current = L.featureGroup().addTo(map);
    markerGroupRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;
    fittedKey.current = null;
    userMoved.current = false;

    // Programmatic fits run inside autoMoving; anything else that starts a move or zoom is the user's.
    const onUserMove = () => {
      if (!autoMoving.current) {
        userMoved.current = true;
      }
    };
    map.on('movestart zoomstart', onUserMove);

    // Window resize, widget size change, density, wall display: re-measure, then re-fit unless the user moved the map.
    let resizeTimer: ReturnType<typeof setTimeout> | null = null;
    const onResize = () => {
      if (resizeTimer) {
        clearTimeout(resizeTimer);
      }
      resizeTimer = setTimeout(() => {
        resizeTimer = null;
        if (mapRef.current !== map || !hasSize(container)) {
          return;
        }
        autoMoving.current = true;
        try {
          map.invalidateSize({ animate: false, pan: false });
        } finally {
          autoMoving.current = false;
        }
        if (!userMoved.current) {
          applyFit.current();
        }
      }, 120);
    };
    const observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(onResize) : null;
    observer?.observe(container);

    return () => {
      observer?.disconnect();
      if (resizeTimer) {
        clearTimeout(resizeTimer);
      }
      map.off('movestart zoomstart', onUserMove);
      map.remove();
      mapRef.current = null;
      tileRef.current = null;
      markerGroupRef.current = null;
      geoGroupRef.current = null;
    };
    // Created once; tiles, layers and the view update in place below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Tiles: swapped in place, so a style change keeps the view and the layers.
  useEffect(() => {
    const map = mapRef.current;
    if (!map) {
      return;
    }
    tileRef.current?.remove();
    tileRef.current = L.tileLayer(tileUrl, { attribution, maxZoom: 19 }).addTo(map);
  }, [tileUrl, attribution]);

  const hidden = useMemo(() => new Set(hiddenLayers ?? []), [hiddenLayers]);
  const shownMarkers = useMemo(
    () => markers.filter((marker) => Number.isFinite(marker.lat) && Number.isFinite(marker.lng) && !(marker.layer && hidden.has(marker.layer))),
    [markers, hidden],
  );
  const visibleGeo = useMemo(() => (geoJsonLayers ?? []).filter((layer) => layer.visible !== false && !!layer.data), [geoJsonLayers]);

  // GeoJSON layers.
  useEffect(() => {
    const group = geoGroupRef.current;
    if (!group) {
      return;
    }
    group.clearLayers();
    for (const layer of visibleGeo) {
      const color = layer.color || DEFAULT_COLOR;
      try {
        L.geoJSON(layer.data, {
          pane: GEO_PANE,
          interactive: false,
          style: () => ({ color, weight: 2, opacity: 0.9, fillColor: color, fillOpacity: 0.15 }),
          pointToLayer: (_feature, latlng) =>
            L.circleMarker(latlng, { pane: GEO_PANE, interactive: false, radius: 4, color: '#fff', weight: 1, fillColor: color, fillOpacity: 0.9 }),
        }).addTo(group);
      } catch (error) {
        console.warn(`MiniMap: GeoJSON layer "${layer.id}" could not be drawn.`, error);
      }
    }
  }, [visibleGeo]);

  // Markers.
  useEffect(() => {
    const group = markerGroupRef.current;
    if (!group) {
      return;
    }
    group.clearLayers();
    for (const marker of shownMarkers) {
      const layer =
        marker.kind === 'station'
          ? L.marker([marker.lat, marker.lng], { icon: stationIcon(), keyboard: false })
          : L.circleMarker([marker.lat, marker.lng], {
              radius: marker.radius ?? 7,
              color: '#fff',
              weight: 2,
              fillColor: marker.color || DEFAULT_COLOR,
              fillOpacity: 1,
            });
      if (marker.tooltip) {
        layer.bindTooltip(tooltipNode(marker.tooltip));
      }
      layer.addTo(group);
    }
  }, [shownMarkers]);

  // The view: fit the markers; with no markers, the GeoJSON layers; with neither, the given center.
  useEffect(() => {
    const geoBounds = geoGroupRef.current && visibleGeo.length > 0 ? geoGroupRef.current.getBounds() : null;
    let key: string | null = null;
    let target: FitTarget | null = null;

    if (shownMarkers.length > 0) {
      if (fit) {
        key = `m:${markers
          .map((marker) => marker.id)
          .sort()
          .join('|')}`;
        target = { bounds: L.latLngBounds(shownMarkers.map((marker) => [marker.lat, marker.lng] as [number, number])).pad(0.15) };
      }
    } else if (geoBounds && geoBounds.isValid()) {
      if (fit) {
        key = `g:${visibleGeo
          .map((layer) => layer.id)
          .sort()
          .join('|')}`;
        target = { bounds: geoBounds.pad(0.1) };
      }
    } else if (center) {
      key = `c:${center[0]},${center[1]},${zoom}`;
      target = { center, zoom };
    }

    if (key && target && fittedKey.current !== key) {
      fittedKey.current = key;
      fitTarget.current = target;
      userMoved.current = false;
      applyFit.current();
    }
  }, [shownMarkers, markers, visibleGeo, fit, center, zoom]);

  return (
    <div className="rg-minimap" role="region" aria-label={ariaLabel}>
      <div className="rg-minimap-canvas" ref={containerRef} />
      {legend ? <div className="rg-map-legend">{legend}</div> : null}
    </div>
  );
}
