import type { FeatureCollection, Geometry } from 'geojson';
import type { DrawHandle, DrawOptions } from './rgDraw';

export type { DrawHandle, DrawOptions } from './rgDraw';

/**
 * resgrid.maps — the map kit the server-rendered (jQuery) pages use (maplibre-mapping-routing-navigation-plan.md §7.1).
 * One API over two engines: MapLibre GL when the department has Maps.MapLibreWeb on (style from the API's signed
 * GetStyle URL), else the Leaflet raster map the pages always used, so each page is written once and the flag decides.
 * Coordinates are [latitude, longitude] and zooms are Leaflet zooms everywhere (MapLibre's are one lower, D24); a
 * MapLibre map that cannot load its style falls back to Leaflet.
 */

export type LatLng = [number, number];

export interface RgMapConfig {
  /** "maplibre" to render with MapLibre GL; anything else renders with Leaflet. */
  renderer?: string;
  styleUrl?: string;
  tileUrl?: string;
  attribution?: string;
  /** Live traffic vector tiles (MapLibre maps only); empty when the install has no traffic provider. */
  trafficUrl?: string;
  trafficRefreshSeconds?: number;
  /** The traffic toggle's localized label. */
  trafficLabel?: string;
}

export interface CreateMapOptions {
  center?: LatLng;
  /** Leaflet zoom. */
  zoom?: number;
  maxZoom?: number;
  scrollZoom?: boolean;
  doubleClickZoom?: boolean;
  /** Defaults to window.resgridMapConfig (written by the _MapKitConfig partial). */
  config?: RgMapConfig;
  /** Add the Records hydrant layer when the user may see it (default true, as the Leaflet init hook did). */
  hydrants?: boolean;
  /** Keep the GL drawing buffer so window.print() captures the map (Routes Directions). */
  printable?: boolean;
  /**
   * A live traffic toggle on the map (§11.1), starting on or off until the user chooses on this device; omitted for
   * pickers and small maps. MapLibre maps with a traffic provider only.
   */
  traffic?: 'on' | 'off';
}

export interface MarkerOptions {
  iconUrl?: string;
  iconSize?: [number, number];
  /** Pixel point of the icon placed on the location (Leaflet iconAnchor); bottom-centre by default. */
  iconAnchor?: [number, number];
  /** Inner HTML of a custom marker (a Leaflet divIcon's html). Trusted markup only. */
  html?: string;
  className?: string;
  /** Permanent text label under the marker (escaped). */
  label?: string;
  /** Popup HTML (callers escape anything user-entered). */
  popupHtml?: string;
  title?: string;
  draggable?: boolean;
  zIndex?: number;
}

export interface MarkerHandle {
  getLatLng(): LatLng;
  setLatLng(position: LatLng): void;
  setPopupHtml(html: string): void;
  openPopup(): void;
  onDragEnd(handler: (position: LatLng) => void): void;
  onClick(handler: () => void): void;
  remove(): void;
}

export interface ShapeOptions {
  color?: string;
  weight?: number;
  opacity?: number;
  fillOpacity?: number;
  dashed?: boolean;
  pointRadius?: number;
}

export interface LayerHandle {
  setData(data: FeatureCollection | Geometry | LatLng[]): void;
  remove(): void;
  /** [south-west, north-east] of what the layer draws, or null when empty. */
  bounds(): [LatLng, LatLng] | null;
}

export interface FitOptions {
  padding?: number;
  /** Leaflet zoom. */
  maxZoom?: number;
  animate?: boolean;
}

export interface RgMap {
  engine: 'maplibre' | 'leaflet';
  /** The underlying maplibregl.Map or L.Map, for page-specific needs. */
  native: unknown;
  setView(center: LatLng, zoom?: number): void;
  getCenter(): LatLng;
  getZoom(): number;
  fitBounds(points: LatLng[], options?: FitOptions): void;
  resize(): void;
  addMarker(position: LatLng, options?: MarkerOptions): MarkerHandle;
  addLine(id: string, data: LatLng[] | Geometry | FeatureCollection, options?: ShapeOptions): LayerHandle;
  addGeoJson(id: string, data: FeatureCollection | Geometry, options?: ShapeOptions): LayerHandle;
  onClick(handler: (position: LatLng) => void): void;
  /** Places a page element (legend, toggle) in a map corner. */
  addControl(element: HTMLElement, position?: 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right'): { remove(): void };
  /**
   * Terra Draw drawing tools (MapLibre maps only; null on Leaflet maps, which keep Leaflet-Geoman on map.native).
   */
  startDrawing(options: DrawOptions): Promise<DrawHandle> | null;
  remove(): void;
}

export interface RouteData {
  Provider: string;
  Attribution: string;
  DistanceMeters: number;
  DurationSeconds: number;
  IsEstimate: boolean;
  IsTrafficAware: boolean;
  /** GeoJSON LineString. */
  Geometry: string | null;
  Legs: { DistanceMeters: number; DurationSeconds: number }[];
  /** The unit's vehicle limits were applied (UnitId given and the provider enforces them). */
  VehicleRestricted?: boolean;
  /** OSRM v5 / Mapbox Directions route object (legs with steps) when steps were asked for. */
  Route: { legs?: { distance: number; duration: number; steps?: unknown[] }[] } | null;
}

declare global {
  interface Window {
    L?: any;
    resgridMapConfig?: RgMapConfig;
    rgHydrantMap?: {
      url: string;
      detailsUrl: string;
      label: string;
      inService: string;
      outOfService: string;
      failure: string;
      retry: string;
    };
  }
}

const DEFAULT_CENTER: LatLng = [39.8283, -98.5795];

function resolveContainer(container: string | HTMLElement): HTMLElement {
  const element = typeof container === 'string' ? document.getElementById(container) ?? document.querySelector<HTMLElement>(container) : container;
  if (!element) {
    throw new Error(`Map container ${String(container)} not found`);
  }

  return element;
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string);
}

function isValid(position: LatLng | null | undefined): position is LatLng {
  return (
    !!position &&
    Number.isFinite(position[0]) &&
    Number.isFinite(position[1]) &&
    Math.abs(position[0]) <= 90 &&
    Math.abs(position[1]) <= 180
  );
}

/** Every [lat, lng] in a geometry, feature collection or point list. */
function collectPoints(data: FeatureCollection | Geometry | LatLng[] | null | undefined): LatLng[] {
  const points: LatLng[] = [];

  const walk = (coordinates: unknown): void => {
    if (!Array.isArray(coordinates)) {
      return;
    }

    if (typeof coordinates[0] === 'number' && typeof coordinates[1] === 'number') {
      points.push([coordinates[1] as number, coordinates[0] as number]);
      return;
    }

    coordinates.forEach(walk);
  };

  if (!data) {
    return points;
  }

  if (Array.isArray(data)) {
    return data.filter((p) => isValid(p));
  }

  if (data.type === 'FeatureCollection') {
    data.features.forEach((feature) => feature.geometry && points.push(...collectPoints(feature.geometry)));
  } else if (data.type === 'GeometryCollection') {
    data.geometries.forEach((geometry) => points.push(...collectPoints(geometry)));
  } else {
    walk((data as { coordinates?: unknown }).coordinates);
  }

  return points;
}

function boundsOf(points: LatLng[]): [LatLng, LatLng] | null {
  const valid = points.filter((p) => isValid(p));
  if (valid.length === 0) {
    return null;
  }

  return [
    [Math.min(...valid.map((p) => p[0])), Math.min(...valid.map((p) => p[1]))],
    [Math.max(...valid.map((p) => p[0])), Math.max(...valid.map((p) => p[1]))],
  ];
}

function toFeatureCollection(data: FeatureCollection | Geometry | LatLng[]): FeatureCollection {
  if (Array.isArray(data)) {
    return {
      type: 'FeatureCollection',
      features: [{ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: data.map((p) => [p[1], p[0]]) } }],
    };
  }

  if (data.type === 'FeatureCollection') {
    return data;
  }

  return { type: 'FeatureCollection', features: [{ type: 'Feature', properties: {}, geometry: data }] };
}

function hydrantPopup(point: Record<string, unknown>): HTMLElement {
  const config = window.rgHydrantMap!;
  const field = (name: string) => (point[name] !== undefined ? point[name] : point[name.charAt(0).toLowerCase() + name.slice(1)]);
  const popup = document.createElement('div');
  const link = document.createElement('a');
  link.href = `${config.detailsUrl}?id=${encodeURIComponent(String(field('HydrantId') ?? ''))}`;
  link.textContent = String(field('HydrantNumber') || config.label);
  popup.appendChild(link);

  const flow = field('FlowGpm');
  if (flow !== null && flow !== undefined) {
    popup.appendChild(document.createElement('br'));
    popup.appendChild(document.createTextNode(`${flow} gpm`));
  }

  popup.appendChild(document.createElement('br'));
  popup.appendChild(document.createTextNode(field('InService') ? config.inService : config.outOfService));
  return popup;
}

let hydrantRequest: Promise<Record<string, unknown>[] | null> | null = null;

function loadHydrants(): Promise<Record<string, unknown>[] | null> {
  const config = window.rgHydrantMap;
  if (!config) {
    return Promise.resolve(null);
  }

  if (!hydrantRequest) {
    hydrantRequest = fetch(config.url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } })
      .then((response) => {
        if (response.status === 403 || response.status === 404) {
          return null;
        }

        if (!response.ok || response.redirected) {
          throw new Error('Hydrant layer unavailable');
        }

        return response.json();
      })
      .catch((error) => {
        hydrantRequest = null;
        throw error;
      });
  }

  return hydrantRequest;
}

// ── MapLibre ─────────────────────────────────────────────────────────────────

type MapLibreModule = typeof import('maplibre-gl');

async function createMapLibre(element: HTMLElement, options: CreateMapOptions, config: RgMapConfig): Promise<RgMap> {
  const [maplibre] = await Promise.all([import('./maplibreLoader').then((m) => m.loadMapLibre()), import('maplibre-gl/dist/maplibre-gl.css')]);
  const center = isValid(options.center) ? options.center : DEFAULT_CENTER;

  const map = new maplibre.Map({
    container: element,
    style: config.styleUrl!,
    center: [center[1], center[0]],
    zoom: Math.max(0, (options.zoom ?? 10) - 1),
    maxZoom: options.maxZoom !== undefined ? Math.max(0, options.maxZoom - 1) : 22,
    scrollZoom: options.scrollZoom !== false,
    doubleClickZoom: options.doubleClickZoom !== false,
    // A hidden browser pane stalls animation frames; without fades the map still finishes drawing.
    fadeDuration: document.hidden ? 0 : 300,
    attributionControl: { compact: true, customAttribution: config.attribution || undefined },
    ...(options.printable ? { canvasContextAttributes: { preserveDrawingBuffer: true } } : {}),
  });

  map.addControl(new maplibre.NavigationControl({ showCompass: false }), 'top-left');

  await new Promise<void>((resolve, reject) => {
    let settled = false;

    // The style and first frame wait on animation frames, which a background tab pauses: only time the page spends
    // visible counts toward the 15 seconds, or a page opened in a background tab would always fall back.
    let remaining = 15000;
    let startedAt = 0;
    let timer = 0;
    const stop = () => {
      if (timer) {
        window.clearTimeout(timer);
        timer = 0;
        remaining -= Date.now() - startedAt;
      }
    };
    const start = () => {
      if (settled || timer || document.hidden) {
        return;
      }
      startedAt = Date.now();
      timer = window.setTimeout(() => settle(new Error('Map style did not load')), Math.max(0, remaining));
    };
    const onVisibility = () => (document.hidden ? stop() : start());
    const settle = (error?: Error) => {
      if (settled) {
        return;
      }
      settled = true;
      stop();
      document.removeEventListener('visibilitychange', onVisibility);
      if (error) {
        reject(error);
      } else {
        resolve();
      }
    };
    document.addEventListener('visibilitychange', onVisibility);
    start();

    map.once('load', () => settle());

    map.on('error', (event) => {
      // A source or tile that fails (a tileset the token may not read, a tile server hiccup) leaves the rest of the
      // map working; only a style that cannot load means there is no map.
      if ((event as { sourceId?: string })?.sourceId) {
        return;
      }
      if (!map.isStyleLoaded()) {
        settle(new Error(event?.error?.message || 'Map style did not load'));
      }
    });
  }).catch((error) => {
    map.remove();
    throw error;
  });

  if (options.traffic && config.trafficUrl) {
    void import('./rgTraffic')
      .then(({ addTrafficToggle }) =>
        addTrafficToggle(map, {
          url: config.trafficUrl!,
          refreshSeconds: config.trafficRefreshSeconds,
          initial: options.traffic === 'on',
          label: config.trafficLabel,
        }),
      )
      // The toggle is optional: the map stays usable without it, so a failed chunk load is logged, not raised.
      .catch((error: unknown) => console.error('Failed to add the live traffic toggle', error));
  }

  const layerIds = new Set<string>();

  const removeArtifacts = (id: string) => {
    for (const suffix of ['fill', 'line', 'circle']) {
      const layerId = `rg-${id}-${suffix}`;
      if (map.getLayer(layerId)) {
        map.removeLayer(layerId);
      }
    }

    if (map.getSource(`rg-${id}`)) {
      map.removeSource(`rg-${id}`);
    }

    layerIds.delete(id);
  };

  const addShapes = (id: string, data: FeatureCollection, shape: ShapeOptions, lineOnly: boolean): LayerHandle => {
    removeArtifacts(id);
    let current = data;
    map.addSource(`rg-${id}`, { type: 'geojson', data });

    const color = shape.color ?? '#1c84c6';
    const dash = shape.dashed ? { 'line-dasharray': [2, 1.5] } : {};

    if (!lineOnly) {
      map.addLayer({
        id: `rg-${id}-fill`,
        type: 'fill',
        source: `rg-${id}`,
        filter: ['==', '$type', 'Polygon'],
        paint: { 'fill-color': ['coalesce', ['get', 'color'], color], 'fill-opacity': shape.fillOpacity ?? 0.1 },
      });
    }

    map.addLayer({
      id: `rg-${id}-line`,
      type: 'line',
      source: `rg-${id}`,
      filter: lineOnly ? ['==', '$type', 'LineString'] : ['in', '$type', 'LineString', 'Polygon'],
      layout: { 'line-join': 'round', 'line-cap': 'round' },
      paint: { 'line-color': ['coalesce', ['get', 'color'], color], 'line-width': shape.weight ?? 2, 'line-opacity': shape.opacity ?? 0.8, ...dash } as any,
    });

    if (!lineOnly) {
      map.addLayer({
        id: `rg-${id}-circle`,
        type: 'circle',
        source: `rg-${id}`,
        filter: ['==', '$type', 'Point'],
        paint: {
          'circle-radius': shape.pointRadius ?? 6,
          'circle-color': ['coalesce', ['get', 'color'], color],
          'circle-opacity': 0.45,
          'circle-stroke-color': ['coalesce', ['get', 'color'], color],
          'circle-stroke-width': 2,
        },
      });
    }

    layerIds.add(id);

    return {
      setData: (next) => {
        current = toFeatureCollection(next);
        (map.getSource(`rg-${id}`) as any)?.setData(current);
      },
      remove: () => removeArtifacts(id),
      bounds: () => boundsOf(collectPoints(current)),
    };
  };

  const rgMap: RgMap = {
    engine: 'maplibre',
    native: map,
    setView: (position, zoom) => {
      if (isValid(position)) {
        map.jumpTo({ center: [position[1], position[0]], zoom: zoom !== undefined ? Math.max(0, zoom - 1) : map.getZoom() });
      }
    },
    getCenter: () => {
      const c = map.getCenter();
      return [c.lat, c.lng];
    },
    getZoom: () => map.getZoom() + 1,
    fitBounds: (points, fit) => {
      const bounds = boundsOf(points);
      if (!bounds) {
        return;
      }

      map.fitBounds(
        [
          [bounds[0][1], bounds[0][0]],
          [bounds[1][1], bounds[1][0]],
        ],
        {
          padding: fit?.padding ?? 40,
          maxZoom: fit?.maxZoom !== undefined ? Math.max(0, fit.maxZoom - 1) : 16,
          animate: fit?.animate ?? false,
        },
      );
    },
    resize: () => map.resize(),
    addMarker: (position, marker = {}) => {
      const wrapper = document.createElement('div');
      wrapper.className = `rg-kit-marker ${marker.className ?? ''}`.trim();
      if (marker.title) {
        wrapper.title = marker.title;
      }
      if (marker.zIndex !== undefined) {
        wrapper.style.zIndex = String(marker.zIndex);
      }

      let anchorX = 0;
      let anchorY = 0;

      if (marker.html) {
        wrapper.innerHTML = marker.html;
        if (marker.iconAnchor) {
          [anchorX, anchorY] = marker.iconAnchor;
        }
      } else if (marker.iconUrl) {
        const size = marker.iconSize ?? [32, 37];
        const image = document.createElement('img');
        image.src = marker.iconUrl;
        image.alt = '';
        image.style.width = `${size[0]}px`;
        image.style.height = `${size[1]}px`;
        image.style.display = 'block';
        wrapper.appendChild(image);
        [anchorX, anchorY] = marker.iconAnchor ?? [size[0] / 2, size[1]];
      }

      let instance: import('maplibre-gl').Marker;
      if (marker.html || marker.iconUrl) {
        wrapper.style.position = 'relative';
        instance = new maplibre.Marker({ element: wrapper, anchor: 'top-left', offset: [-anchorX, -anchorY], draggable: !!marker.draggable });
      } else {
        instance = new maplibre.Marker({ draggable: !!marker.draggable });
        if (marker.title) {
          instance.getElement().title = marker.title;
        }
      }

      if (marker.label) {
        const label = document.createElement('div');
        label.className = 'rg-kit-marker-label';
        label.innerHTML = escapeHtml(marker.label);
        label.style.cssText =
          'position:absolute;left:50%;top:100%;transform:translateX(-50%);white-space:nowrap;background:rgba(255,255,255,.9);border-radius:3px;padding:0 4px;font-size:11px;font-weight:600;color:#333;box-shadow:0 1px 2px rgba(0,0,0,.25);pointer-events:none;';
        instance.getElement().appendChild(label);
      }

      // A click on a marker opens its popup; it must never also count as a click on the map (which places pins).
      instance.getElement().addEventListener('click', (event) => event.stopPropagation());
      instance.setLngLat([position[1], position[0]]).addTo(map);

      const setPopup = (html: string) => {
        instance.setPopup(new maplibre.Popup({ offset: 24, maxWidth: '320px' }).setHTML(html));
      };

      if (marker.popupHtml) {
        setPopup(marker.popupHtml);
      }

      return {
        getLatLng: () => {
          const p = instance.getLngLat();
          return [p.lat, p.lng];
        },
        setLatLng: (next) => {
          if (isValid(next)) {
            instance.setLngLat([next[1], next[0]]);
          }
        },
        setPopupHtml: setPopup,
        openPopup: () => {
          const popup = instance.getPopup();
          if (popup && !popup.isOpen()) {
            instance.togglePopup();
          }
        },
        onDragEnd: (handler) => {
          instance.on('dragend', () => {
            const p = instance.getLngLat();
            handler([p.lat, p.lng]);
          });
        },
        onClick: (handler) => {
          instance.getElement().addEventListener('click', (event) => {
            event.stopPropagation();
            handler();
          });
        },
        remove: () => instance.remove(),
      };
    },
    addLine: (id, data, shape = {}) => addShapes(id, toFeatureCollection(data as FeatureCollection | Geometry | LatLng[]), { weight: 4, ...shape }, true),
    addGeoJson: (id, data, shape = {}) => addShapes(id, toFeatureCollection(data), shape, false),
    onClick: (handler) => {
      map.on('click', (event) => handler([event.lngLat.lat, event.lngLat.lng]));
    },
    startDrawing: (drawOptions) => import('./rgDraw').then((module) => module.startDrawing(map, drawOptions)),
    addControl: (element, position = 'top-left') => {
      element.classList.add('maplibregl-ctrl');
      const control = { onAdd: () => element, onRemove: () => element.remove() };
      map.addControl(control, position);
      return { remove: () => map.removeControl(control) };
    },
    remove: () => map.remove(),
  };

  if (options.hydrants !== false && window.rgHydrantMap) {
    addMapLibreHydrants(maplibre, map);
  }

  const resize = () => map.resize();
  window.addEventListener('resize', resize);
  map.once('remove', () => window.removeEventListener('resize', resize));

  return rgMap;
}

/** The Records hydrant layer the Leaflet init hook adds to every Leaflet map, for MapLibre maps. */
function addMapLibreHydrants(maplibre: MapLibreModule, map: import('maplibre-gl').Map): void {
  const config = window.rgHydrantMap!;
  let points: Record<string, unknown>[] = [];

  loadHydrants()
    .then((loaded) => {
      if (!loaded || !Array.isArray(loaded)) {
        return;
      }

      points = loaded;
      const field = (point: Record<string, unknown>, name: string) =>
        point[name] !== undefined ? point[name] : point[name.charAt(0).toLowerCase() + name.slice(1)];

      const features = loaded
        .map((point, index) => {
          const latitude = Number(field(point, 'Latitude'));
          const longitude = Number(field(point, 'Longitude'));
          if (!Number.isFinite(latitude) || !Number.isFinite(longitude) || Math.abs(latitude) > 90 || Math.abs(longitude) > 180) {
            return null;
          }

          return {
            type: 'Feature' as const,
            properties: { index, color: String(field(point, 'Color') || '#999999') },
            geometry: { type: 'Point' as const, coordinates: [longitude, latitude] },
          };
        })
        .filter((feature): feature is NonNullable<typeof feature> => feature !== null);

      map.addSource('rg-hydrants', { type: 'geojson', data: { type: 'FeatureCollection', features } });
      map.addLayer({
        id: 'rg-hydrants',
        type: 'circle',
        source: 'rg-hydrants',
        paint: {
          'circle-radius': 7,
          'circle-color': ['get', 'color'],
          'circle-opacity': 0.85,
          'circle-stroke-color': ['get', 'color'],
          'circle-stroke-width': 2,
        },
      });

      map.on('click', 'rg-hydrants', (event) => {
        const feature = event.features?.[0];
        const point = feature ? points[Number(feature.properties?.index)] : null;
        if (point) {
          new maplibre.Popup({ offset: 10 }).setLngLat(event.lngLat).setDOMContent(hydrantPopup(point)).addTo(map);
        }
      });
      map.on('mouseenter', 'rg-hydrants', () => (map.getCanvas().style.cursor = 'pointer'));
      map.on('mouseleave', 'rg-hydrants', () => (map.getCanvas().style.cursor = ''));

      // A collapsed toggle, as the Leaflet layer control was.
      const control = document.createElement('div');
      control.className = 'maplibregl-ctrl maplibregl-ctrl-group';
      control.style.cssText = 'padding:4px 8px;font-size:12px;';
      const label = document.createElement('label');
      label.style.cssText = 'margin:0;font-weight:normal;cursor:pointer;';
      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.checked = true;
      checkbox.style.marginRight = '4px';
      checkbox.addEventListener('change', () => map.setLayoutProperty('rg-hydrants', 'visibility', checkbox.checked ? 'visible' : 'none'));
      label.appendChild(checkbox);
      label.appendChild(document.createTextNode(config.label));
      control.appendChild(label);
      map.addControl({ onAdd: () => control, onRemove: () => control.remove() }, 'top-right');
    })
    .catch(() => {
      const box = document.createElement('div');
      box.className = 'maplibregl-ctrl maplibregl-ctrl-group';
      box.setAttribute('role', 'alert');
      box.style.cssText = 'background:white;padding:8px;max-width:240px;';
      box.appendChild(document.createTextNode(`${config.failure} `));
      const retry = document.createElement('button');
      retry.type = 'button';
      retry.className = 'btn btn-xs btn-default';
      retry.textContent = config.retry;
      box.appendChild(retry);
      const errorControl = { onAdd: () => box, onRemove: () => box.remove() };
      retry.onclick = () => {
        map.removeControl(errorControl);
        addMapLibreHydrants(maplibre, map);
      };
      map.addControl(errorControl, 'bottom-left');
    });
}

// ── Leaflet ──────────────────────────────────────────────────────────────────

function createLeaflet(element: HTMLElement, options: CreateMapOptions, config: RgMapConfig): RgMap {
  const L = window.L;
  if (!L) {
    throw new Error('Leaflet is not loaded');
  }

  const center = isValid(options.center) ? options.center : DEFAULT_CENTER;
  const map = L.map(element, {
    scrollWheelZoom: options.scrollZoom !== false,
    doubleClickZoom: options.doubleClickZoom !== false,
    maxZoom: options.maxZoom ?? 19,
  }).setView(center, options.zoom ?? 10);

  L.tileLayer(config.tileUrl || 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: options.maxZoom ?? 19,
    attribution: config.attribution || '',
  }).addTo(map);

  const layers = new Map<string, any>();

  const addShapes = (id: string, data: FeatureCollection, shape: ShapeOptions, line: boolean): LayerHandle => {
    layers.get(id)?.remove();
    let current = data;
    const color = shape.color ?? '#1c84c6';
    const style = (feature?: { properties?: Record<string, unknown> }) => {
      const featureColor = (feature?.properties?.color as string) || color;
      return {
        color: featureColor,
        weight: shape.weight ?? (line ? 4 : 2),
        opacity: shape.opacity ?? (line ? 0.8 : 0.7),
        fillColor: featureColor,
        fillOpacity: shape.fillOpacity ?? 0.1,
        dashArray: shape.dashed ? '8 6' : undefined,
      };
    };

    const layer = L.geoJSON(data, {
      style,
      pointToLayer: (_feature: unknown, latlng: unknown) => L.circleMarker(latlng, { radius: shape.pointRadius ?? 6 }),
    }).addTo(map);
    layers.set(id, layer);

    return {
      setData: (next) => {
        current = toFeatureCollection(next);
        layer.clearLayers();
        layer.addData(current);
      },
      remove: () => {
        layer.remove();
        layers.delete(id);
      },
      bounds: () => boundsOf(collectPoints(current)),
    };
  };

  return {
    engine: 'leaflet',
    native: map,
    setView: (position, zoom) => {
      if (isValid(position)) {
        map.setView(position, zoom ?? map.getZoom());
      }
    },
    getCenter: () => {
      const c = map.getCenter();
      return [c.lat, c.lng];
    },
    getZoom: () => map.getZoom(),
    fitBounds: (points, fit) => {
      const bounds = boundsOf(points);
      if (bounds) {
        const padding = fit?.padding ?? 40;
        map.fitBounds(bounds, { padding: [padding, padding], maxZoom: fit?.maxZoom ?? 16, animate: fit?.animate ?? false });
      }
    },
    resize: () => map.invalidateSize(),
    addMarker: (position, marker = {}) => {
      let icon: unknown;
      if (marker.html) {
        icon = L.divIcon({ className: marker.className ?? '', html: marker.html, iconSize: marker.iconSize ?? null, iconAnchor: marker.iconAnchor });
      } else if (marker.iconUrl) {
        const size = marker.iconSize ?? [32, 37];
        icon = L.icon({ iconUrl: marker.iconUrl, iconSize: size, iconAnchor: marker.iconAnchor ?? [size[0] / 2, size[1]], popupAnchor: [0, -size[1]] });
      }

      const instance = L.marker(position, {
        ...(icon ? { icon } : {}),
        draggable: !!marker.draggable,
        title: marker.title ?? '',
        zIndexOffset: marker.zIndex ?? 0,
      }).addTo(map);

      if (marker.label) {
        instance.bindTooltip(escapeHtml(marker.label), { permanent: true, direction: 'bottom', className: 'rg-kit-marker-label' });
      }

      if (marker.popupHtml) {
        instance.bindPopup(marker.popupHtml);
      }

      // A click on a marker must not also count as a click on the map (which places pins).
      instance.options.bubblingMouseEvents = false;

      return {
        getLatLng: () => {
          const p = instance.getLatLng();
          return [p.lat, p.lng];
        },
        setLatLng: (next) => {
          if (isValid(next)) {
            instance.setLatLng(next);
          }
        },
        setPopupHtml: (html) => {
          if (instance.getPopup()) {
            instance.setPopupContent(html);
          } else {
            instance.bindPopup(html);
          }
        },
        openPopup: () => instance.openPopup(),
        onDragEnd: (handler) => {
          instance.on('dragend', () => {
            const p = instance.getLatLng();
            handler([p.lat, p.lng]);
          });
        },
        onClick: (handler) => {
          instance.on('click', handler);
        },
        remove: () => instance.remove(),
      };
    },
    addLine: (id, data, shape = {}) => addShapes(id, toFeatureCollection(data as FeatureCollection | Geometry | LatLng[]), shape, true),
    addGeoJson: (id, data, shape = {}) => addShapes(id, toFeatureCollection(data), shape, false),
    onClick: (handler) => {
      map.on('click', (event: { latlng: { lat: number; lng: number } }) => handler([event.latlng.lat, event.latlng.lng]));
    },
    startDrawing: () => null,
    addControl: (element, position = 'top-left') => {
      const control = L.control({ position: position.replace('-', '') });
      control.onAdd = () => {
        L.DomEvent.disableClickPropagation(element);
        return element;
      };
      control.addTo(map);
      return { remove: () => control.remove() };
    },
    remove: () => map.remove(),
  };
}

// ── Public API ───────────────────────────────────────────────────────────────

/** Creates a map in the container with the engine the department uses. */
export async function create(container: string | HTMLElement, options: CreateMapOptions = {}): Promise<RgMap> {
  const element = resolveContainer(container);
  const config = { ...(window.resgridMapConfig ?? {}), ...(options.config ?? {}) };

  if (config.renderer === 'maplibre' && config.styleUrl) {
    try {
      return await createMapLibre(element, options, config);
    } catch (error) {
      // The base map still has to show: fall back to the raster map.
      console.error('MapLibre map unavailable, using the raster map', error);
      element.innerHTML = '';
    }
  }

  return createLeaflet(element, options, config);
}

/**
 * A route through two or more points from the install's routing provider (v4 Routing/GetRoute through the web BFF):
 * no client ever calls a routing service directly (§6.3). Null when no route came back.
 */
export async function route(
  points: LatLng[],
  options: { profile?: number; avoid?: number; unitId?: number; steps?: boolean; metric?: boolean } = {},
): Promise<RouteData | null> {
  const valid = points.filter((p) => isValid(p));
  if (valid.length < 2) {
    return null;
  }

  // The web BFF validates the antiforgery token on every POST, as the shared API client does.
  const headers: Record<string, string> = { 'Content-Type': 'application/json', Accept: 'application/json' };
  const verification = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content;
  if (verification) {
    headers.RequestVerificationToken = verification;
  }

  const response = await fetch('/api/web-bff/api/v4/Routing/GetRoute', {
    method: 'POST',
    credentials: 'same-origin',
    headers,
    body: JSON.stringify({
      Points: valid.map((p) => ({ Latitude: p[0], Longitude: p[1], IsStop: true })),
      Profile: options.profile ?? 0,
      Avoid: options.avoid ?? 0,
      UnitId: options.unitId ?? null,
      Steps: options.steps !== false,
      Language: (document.documentElement.lang || navigator.language || 'en').split('-')[0],
      Metric: options.metric ?? !/^en-US$/i.test(navigator.language || ''),
    }),
  });

  if (!response.ok) {
    return null;
  }

  const body = await response.json();
  const data = body?.Data as RouteData | undefined;
  return data && data.Geometry ? data : null;
}

/** Points of a GeoJSON geometry string or object, as [lat, lng]. */
export function geometryPoints(geometry: string | Geometry | null | undefined): LatLng[] {
  if (!geometry) {
    return [];
  }

  try {
    return collectPoints(typeof geometry === 'string' ? (JSON.parse(geometry) as Geometry) : geometry);
  } catch {
    return [];
  }
}

/**
 * Click-to-place, draggable single pin that writes the chosen point back (Dispatch call location, route start/end/stop).
 * Returns setPosition so pages can move the pin after a geocode.
 */
export function locationPicker(
  map: RgMap,
  initial: LatLng | null,
  onChange: (position: LatLng) => void,
  marker: MarkerOptions = {},
): { setPosition(position: LatLng, pan?: boolean): void; clear(): void } {
  let pin: MarkerHandle | null = null;

  const place = (position: LatLng, notify: boolean) => {
    if (!isValid(position)) {
      return;
    }

    if (pin) {
      pin.setLatLng(position);
    } else {
      pin = map.addMarker(position, { ...marker, draggable: true });
      pin.onDragEnd((moved) => onChange(moved));
    }

    if (notify) {
      onChange(position);
    }
  };

  if (initial && isValid(initial)) {
    place(initial, false);
  }

  map.onClick((position) => place(position, true));

  return {
    setPosition: (position, pan = true) => {
      place(position, false);
      if (pan && isValid(position)) {
        map.setView(position, Math.max(map.getZoom(), 15));
      }
    },
    clear: () => {
      pin?.remove();
      pin = null;
    },
  };
}

export const rgMaps = { create, route, geometryPoints, locationPicker };
