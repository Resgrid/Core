/**
 * The live traffic overlay and its map toggle (maplibre-mapping-routing-navigation-plan.md §11.1): Mapbox Traffic v1
 * vector tiles drawn as congestion-coloured lines above the roads and under the labels, offset to each side of two-way
 * roads, reloaded every refresh interval while shown, and the on/off choice remembered on this device. MapLibre maps
 * only; the Leaflet raster maps have no vector tiles to draw it with.
 */

import { ensureControlStyles } from './rgControlStyles';

type GlMap = import('maplibre-gl').Map;

export interface TrafficOptions {
  /** Traffic vector tile URL template ({z}/{x}/{y}) with its token, from the server config. */
  url: string;
  /** Seconds between reloads while shown; the provider updates about every 8 minutes. */
  refreshSeconds?: number;
  /** Shown when the user has never chosen on this device. */
  initial: boolean;
  /** Button title and label. */
  label?: string;
}

export interface TrafficHandle {
  set(on: boolean): void;
  isOn(): boolean;
}

const SOURCE = 'rg-traffic';
const LINE = 'rg-traffic-line';
const CLOSED = 'rg-traffic-closed';
const STORAGE_KEY = 'rg.maps.traffic';

function remembered(): boolean | null {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return value === '1' ? true : value === '0' ? false : null;
  } catch {
    return null;
  }
}

function remember(on: boolean) {
  try {
    window.localStorage.setItem(STORAGE_KEY, on ? '1' : '0');
  } catch {
    // Private windows and blocked storage: the choice lasts for this page only.
  }
}

/** The tiles with a cache-busting minute stamp, so a reload fetches fresh congestion. */
function tilesFor(url: string): string[] {
  const stamp = Math.floor(Date.now() / 60000);
  return [url + (url.includes('?') ? '&' : '?') + 'rgts=' + stamp];
}

/** Draws the overlay (hidden until switched on) and adds the toggle button. Null when the map has no style yet. */
export function addTrafficToggle(map: GlMap, options: TrafficOptions): TrafficHandle | null {
  if (!options.url || !map.getStyle()) {
    return null;
  }

  ensureControlStyles();
  let on = remembered() ?? options.initial;
  let timer: number | null = null;

  const ensureLayers = () => {
    if (map.getSource(SOURCE)) {
      return;
    }

    // Under the first label layer, so street names stay readable over the colours.
    const labels = (map.getStyle().layers ?? []).find((layer) => layer.type === 'symbol')?.id;

    map.addSource(SOURCE, { type: 'vector', tiles: tilesFor(options.url), minzoom: 0, maxzoom: 16 });
    const offset = ['interpolate', ['linear'], ['zoom'], 10, 1, 16, 4] as any;
    const width = ['interpolate', ['linear'], ['zoom'], 10, 1.5, 16, 4] as any;
    map.addLayer(
      {
        id: LINE,
        type: 'line',
        source: SOURCE,
        'source-layer': 'traffic',
        minzoom: 10,
        filter: ['!=', ['get', 'closed'], 'yes'],
        layout: { 'line-cap': 'round', 'line-join': 'round', visibility: on ? 'visible' : 'none' },
        paint: {
          'line-color': ['match', ['get', 'congestion'], 'low', '#2ecc71', 'moderate', '#f1c40f', 'heavy', '#e67e22', 'severe', '#c0392b', '#2ecc71'],
          'line-width': width,
          'line-offset': offset,
          'line-opacity': 0.85,
        },
      },
      labels,
    );
    map.addLayer(
      {
        id: CLOSED,
        type: 'line',
        source: SOURCE,
        'source-layer': 'traffic',
        minzoom: 10,
        filter: ['==', ['get', 'closed'], 'yes'],
        layout: { 'line-cap': 'butt', visibility: on ? 'visible' : 'none' },
        paint: { 'line-color': '#7b0d0d', 'line-width': width, 'line-offset': offset, 'line-dasharray': [1.5, 1] },
      },
      labels,
    );
  };

  const refresh = () => {
    if (!on || document.hidden) {
      return;
    }
    const source = map.getSource(SOURCE) as (import('maplibre-gl').VectorTileSource & { setTiles?: (tiles: string[]) => void }) | undefined;
    source?.setTiles?.(tilesFor(options.url));
  };

  const apply = () => {
    ensureLayers();
    for (const id of [LINE, CLOSED]) {
      if (map.getLayer(id)) {
        map.setLayoutProperty(id, 'visibility', on ? 'visible' : 'none');
      }
    }
    if (timer) {
      window.clearInterval(timer);
      timer = null;
    }
    if (on) {
      timer = window.setInterval(refresh, Math.max(60, options.refreshSeconds ?? 300) * 1000);
    }
    button.classList.toggle('active', on);
    button.setAttribute('aria-pressed', on ? 'true' : 'false');
  };

  const label = options.label || 'Traffic';
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'rg-traffic-toggle';
  button.title = label;
  button.setAttribute('aria-label', label);
  button.innerHTML = '<i class="fa fa-car" aria-hidden="true"></i>';
  button.addEventListener('click', (event) => {
    event.preventDefault();
    on = !on;
    remember(on);
    apply();
  });

  const bar = document.createElement('div');
  bar.className = 'maplibregl-ctrl maplibregl-ctrl-group';
  bar.appendChild(button);
  map.addControl({ onAdd: () => bar, onRemove: () => bar.remove() }, 'top-right');

  // A style switch (day/night) drops added layers; draw them again on the new style.
  map.on('style.load', apply);
  map.on('remove', () => {
    if (timer) {
      window.clearInterval(timer);
    }
  });

  apply();

  return {
    set: (value: boolean) => {
      on = value;
      apply();
    },
    isOn: () => on,
  };
}
