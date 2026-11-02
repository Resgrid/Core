import { useEffect, useMemo, useRef } from 'react';
import type { Feature, FeatureCollection } from 'geojson';
import type { MiniMapProps } from './MiniMapView';

type MapLibreModule = typeof import('maplibre-gl');
type GlMap = import('maplibre-gl').Map;
type Bounds = [[number, number], [number, number]];
type FitTarget = { bounds: Bounds } | { center: [number, number]; zoom: number };

const DEFAULT_COLOR = '#228BCB';

/**
 * The dashboard mini map on MapLibre GL (maplibre-mapping-routing-navigation-plan.md §22.2), for departments on
 * Maps.MapLibreWeb: the department's day or night style, GeoJSON under the markers, a fit that a resize re-applies
 * unless the user moved the map, and no animation (a hidden pane stalls requestAnimationFrame). Zooms are Leaflet
 * zooms on the way in (GL zoom is one lower, D24).
 */
export default function MiniMapLibre(props: MiniMapProps & { styleUrl: string }) {
  const { markers, geoJsonLayers, center, zoom = 13, fit = true, hiddenLayers, ariaLabel, legend, styleUrl } = props;
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<GlMap | null>(null);
  const libRef = useRef<MapLibreModule | null>(null);
  const markerRefs = useRef<import('maplibre-gl').Marker[]>([]);
  const fittedKey = useRef<string | null>(null);
  const fitTarget = useRef<FitTarget | null>(null);
  const userMoved = useRef(false);
  const autoMoving = useRef(false);
  const readyRef = useRef(false);
  const redraw = useRef<() => void>(() => {});

  const hidden = useMemo(() => new Set(hiddenLayers ?? []), [hiddenLayers]);
  const shownMarkers = useMemo(
    () => markers.filter((marker) => Number.isFinite(marker.lat) && Number.isFinite(marker.lng) && !(marker.layer && hidden.has(marker.layer))),
    [markers, hidden],
  );
  const visibleGeo = useMemo(() => (geoJsonLayers ?? []).filter((layer) => layer.visible !== false && !!layer.data), [geoJsonLayers]);

  const applyFit = () => {
    const map = mapRef.current;
    const target = fitTarget.current;
    const container = containerRef.current;
    if (!map || !target || !container || container.clientWidth === 0 || container.clientHeight === 0) {
      return;
    }

    autoMoving.current = true;
    try {
      if ('bounds' in target) {
        map.fitBounds(
          [
            [target.bounds[0][1], target.bounds[0][0]],
            [target.bounds[1][1], target.bounds[1][0]],
          ],
          { maxZoom: 14, animate: false, padding: 16 },
        );
      } else {
        map.jumpTo({ center: [target.center[1], target.center[0]], zoom: Math.max(0, target.zoom - 1) });
      }
    } finally {
      autoMoving.current = false;
    }
  };

  // The map: created once per style.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return undefined;
    }

    let cancelled = false;
    let map: GlMap | null = null;
    let observer: ResizeObserver | null = null;
    let resizeTimer: ReturnType<typeof setTimeout> | null = null;

    void (async () => {
      const [maplibre] = await Promise.all([import('../../../maps/maplibreLoader').then((m) => m.loadMapLibre()), import('maplibre-gl/dist/maplibre-gl.css')]);
      if (cancelled) {
        return;
      }

      libRef.current = maplibre;
      const start = center ?? [39.5, -98.35];
      map = new maplibre.Map({
        container,
        style: styleUrl,
        center: [start[1], start[0]],
        zoom: Math.max(0, (center ? zoom : 4) - 1),
        scrollZoom: false,
        fadeDuration: 0,
        attributionControl: { compact: true },
      });
      map.addControl(new maplibre.NavigationControl({ showCompass: false }), 'top-left');
      mapRef.current = map;

      map.on('movestart', () => {
        if (!autoMoving.current) {
          userMoved.current = true;
        }
      });

      map.once('load', () => {
        if (cancelled) {
          return;
        }
        readyRef.current = true;
        redraw.current();
      });

      observer =
        typeof ResizeObserver !== 'undefined'
          ? new ResizeObserver(() => {
              if (resizeTimer) {
                clearTimeout(resizeTimer);
              }
              resizeTimer = setTimeout(() => {
                map?.resize();
                if (!userMoved.current) {
                  applyFit();
                }
              }, 120);
            })
          : null;
      observer?.observe(container);
    })();

    return () => {
      cancelled = true;
      observer?.disconnect();
      if (resizeTimer) {
        clearTimeout(resizeTimer);
      }
      markerRefs.current.forEach((marker) => marker.remove());
      markerRefs.current = [];
      map?.remove();
      mapRef.current = null;
      readyRef.current = false;
      fittedKey.current = null;
    };
    // The view and layers update in place below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [styleUrl]);

  // Layers, markers and the fit, redrawn when the data changes (and once the style has loaded).
  redraw.current = () => {
    const map = mapRef.current;
    const maplibre = libRef.current;
    if (!map || !maplibre || !readyRef.current) {
      return;
    }

    const geo: FeatureCollection = {
      type: 'FeatureCollection',
      features: visibleGeo.flatMap((layer) => {
        const data = layer.data as unknown as FeatureCollection | Feature | { type: string };
        const color = layer.color || DEFAULT_COLOR;
        const features: Feature[] =
          data.type === 'FeatureCollection'
            ? (data as FeatureCollection).features
            : data.type === 'Feature'
              ? [data as Feature]
              : [{ type: 'Feature', properties: {}, geometry: data as any }];
        return features.map((feature) => ({ ...feature, properties: { ...(feature.properties ?? {}), __color: color } }));
      }),
    };

    if (map.getSource('rg-minimap-geo')) {
      (map.getSource('rg-minimap-geo') as any).setData(geo);
    } else {
      map.addSource('rg-minimap-geo', { type: 'geojson', data: geo });
      map.addLayer({ id: 'rg-minimap-fill', type: 'fill', source: 'rg-minimap-geo', filter: ['==', '$type', 'Polygon'], paint: { 'fill-color': ['get', '__color'], 'fill-opacity': 0.15 } });
      map.addLayer({ id: 'rg-minimap-line', type: 'line', source: 'rg-minimap-geo', filter: ['in', '$type', 'LineString', 'Polygon'], paint: { 'line-color': ['get', '__color'], 'line-width': 2, 'line-opacity': 0.9 } });
      map.addLayer({
        id: 'rg-minimap-point',
        type: 'circle',
        source: 'rg-minimap-geo',
        filter: ['==', '$type', 'Point'],
        paint: { 'circle-radius': 4, 'circle-color': ['get', '__color'], 'circle-stroke-color': '#fff', 'circle-stroke-width': 1 },
      });
    }

    markerRefs.current.forEach((marker) => marker.remove());
    markerRefs.current = [];
    for (const marker of shownMarkers) {
      const element = document.createElement('span');
      if (marker.kind === 'station') {
        element.className = 'rg-minimap-station';
        const icon = document.createElement('i');
        icon.className = 'fa fa-home';
        element.appendChild(icon);
      } else {
        const size = (marker.radius ?? 7) * 2;
        element.style.cssText = `display:block;width:${size}px;height:${size}px;border-radius:50%;border:2px solid #fff;box-sizing:border-box;background:${marker.color || DEFAULT_COLOR};box-shadow:0 0 1px rgba(0,0,0,.6);`;
      }
      if (marker.tooltip) {
        element.title = marker.tooltip;
      }
      markerRefs.current.push(new maplibre.Marker({ element }).setLngLat([marker.lng, marker.lat]).addTo(map));
    }

    // The view: fit the markers; with no markers, the GeoJSON; with neither, the given center.
    const geoPoints: [number, number][] = [];
    const walk = (coordinates: unknown): void => {
      if (Array.isArray(coordinates) && typeof coordinates[0] === 'number') {
        geoPoints.push([coordinates[1] as number, coordinates[0] as number]);
      } else if (Array.isArray(coordinates)) {
        coordinates.forEach(walk);
      }
    };
    geo.features.forEach((feature) => feature.geometry && walk((feature.geometry as any).coordinates));

    const boundsOf = (points: [number, number][], pad: number): Bounds => {
      const lats = points.map((p) => p[0]);
      const lngs = points.map((p) => p[1]);
      const dLat = (Math.max(...lats) - Math.min(...lats)) * pad;
      const dLng = (Math.max(...lngs) - Math.min(...lngs)) * pad;
      return [
        [Math.min(...lats) - dLat, Math.min(...lngs) - dLng],
        [Math.max(...lats) + dLat, Math.max(...lngs) + dLng],
      ];
    };

    let key: string | null = null;
    let target: FitTarget | null = null;
    if (shownMarkers.length > 0) {
      if (fit) {
        key = `m:${markers.map((marker) => marker.id).sort().join('|')}`;
        target = { bounds: boundsOf(shownMarkers.map((marker) => [marker.lat, marker.lng]), 0.15) };
      }
    } else if (geoPoints.length > 0) {
      if (fit) {
        key = `g:${visibleGeo.map((layer) => layer.id).sort().join('|')}`;
        target = { bounds: boundsOf(geoPoints, 0.1) };
      }
    } else if (center) {
      key = `c:${center[0]},${center[1]},${zoom}`;
      target = { center, zoom };
    }

    if (key && target && fittedKey.current !== key) {
      fittedKey.current = key;
      fitTarget.current = target;
      userMoved.current = false;
      applyFit();
    }
  };

  useEffect(() => {
    redraw.current();
  }, [shownMarkers, markers, visibleGeo, fit, center, zoom]);

  return (
    <div className="rg-minimap" role="region" aria-label={ariaLabel}>
      <div className="rg-minimap-canvas" ref={containerRef} />
      {legend ? <div className="rg-map-legend">{legend}</div> : null}
    </div>
  );
}
