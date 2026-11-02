import type { Feature, FeatureCollection, Geometry, MultiPolygon, Point, Polygon, Position } from 'geojson';
import { ensureControlStyles } from './rgControlStyles';

/**
 * Drawing on MapLibre maps with Terra Draw (maplibre-mapping-routing-navigation-plan.md D22, §22.4): the replacement
 * for Leaflet-Geoman on resgrid.maps maps. Leaflet maps keep Geoman, so pages branch on map.engine.
 *
 * Resgrid extensions over Terra Draw, for the tools Geoman had and Terra Draw does not:
 * - text: a point with a `text` property, shown as a label;
 * - cut: draw a polygon over the selected polygon to cut it away (holes, or a split into several parts);
 * - rotate: drag to rotate the selected shape;
 * - circles stored as a Point with a `radius` in metres (`circleAsPoint`), as Geoman saved them.
 * Terra Draw cannot edit polygons with holes or multipolygons, so those are kept beside it: drawn, selectable,
 * deletable, cuttable, but not reshaped.
 */

export type DrawModeName = 'polygon' | 'rectangle' | 'linestring' | 'point' | 'circle' | 'text';
type ToolName = DrawModeName | 'select' | 'delete' | 'clear' | 'cut' | 'rotate';

export interface DrawOptions {
  modes: DrawModeName[];
  /** At most one shape: drawing a new one replaces the old (group geofence, custom map bounds). */
  single?: boolean;
  initial?: FeatureCollection | Geometry | null;
  /** Called with every shape after a shape is drawn, moved, reshaped or deleted. */
  onChange?: (features: Feature[]) => void;
  /** Button titles and prompts (localized by the page); English defaults. */
  labels?: Partial<Record<ToolName | 'textPrompt' | 'selectFirst', string>>;
  color?: string;
  /** Adds the cut tool. */
  cut?: boolean;
  /** Adds the rotate tool. */
  rotate?: boolean;
  /** New vertices snap to existing vertices and lines. */
  snapping?: boolean;
  /** Circles load from, and come back as, a Point with `radius` (metres) and `shape: "Circle"`. */
  circleAsPoint?: boolean;
}

export interface DrawHandle {
  setMode(mode: DrawModeName | 'select'): void;
  /** The active tool: a draw mode name, or "select". */
  getMode(): string;
  /** Every shape. `properties.mode` says what drew it; "static" marks a polygon with holes or a multipolygon. */
  getFeatures(): Feature[];
  clear(): void;
  destroy(): void;
}

const ICONS: Record<ToolName, string> = {
  polygon: 'fa-object-ungroup',
  rectangle: 'fa-square-o',
  linestring: 'fa-long-arrow-right',
  point: 'fa-map-marker',
  circle: 'fa-circle-o',
  text: 'fa-font',
  cut: 'fa-scissors',
  rotate: 'fa-repeat',
  select: 'fa-mouse-pointer',
  delete: 'fa-trash',
  clear: 'fa-times',
};

const TITLES: Record<ToolName | 'textPrompt' | 'selectFirst', string> = {
  polygon: 'Draw a polygon',
  rectangle: 'Draw a rectangle',
  linestring: 'Draw a line',
  point: 'Place a point',
  circle: 'Draw a circle',
  text: 'Add text',
  cut: 'Cut the selected shape',
  rotate: 'Rotate the selected shape',
  select: 'Select and edit',
  delete: 'Delete the selected shape',
  clear: 'Remove all shapes',
  textPrompt: 'Text',
  selectFirst: 'Select a shape first.',
};

const STATIC_SOURCE = 'rg-draw-static';

// Terra Draw refuses any feature with more decimals than this; map clicks, stored shapes, generated circles and
// clipping results all carry more, so everything handed to addFeatures goes through rounded().
const COORDINATE_PRECISION = 9;

function rounded<T extends Geometry>(geometry: T): T {
  const scale = 10 ** COORDINATE_PRECISION;
  const round = (c: unknown): unknown =>
    Array.isArray(c) && typeof c[0] === 'number' ? (c as number[]).map((n) => Math.round(n * scale) / scale) : (c as unknown[]).map(round);
  return { ...geometry, coordinates: round((geometry as { coordinates: unknown }).coordinates) } as T;
}

function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

/** A geodesic circle as a closed ring of 64 points. */
export function circleRing(center: Position, radiusMeters: number): Position[] {
  const earth = 6371008.8;
  const lat = (center[1] * Math.PI) / 180;
  const lng = (center[0] * Math.PI) / 180;
  const d = radiusMeters / earth;
  const ring: Position[] = [];
  for (let i = 0; i < 64; i++) {
    const bearing = (i / 64) * 2 * Math.PI;
    const lat2 = Math.asin(Math.sin(lat) * Math.cos(d) + Math.cos(lat) * Math.sin(d) * Math.cos(bearing));
    const lng2 = lng + Math.atan2(Math.sin(bearing) * Math.sin(d) * Math.cos(lat), Math.cos(d) - Math.sin(lat) * Math.sin(lat2));
    ring.push([(lng2 * 180) / Math.PI, (lat2 * 180) / Math.PI]);
  }
  ring.push(ring[0]);
  return ring;
}

/** Centre and radius (metres) of a circle ring: the vertex average, and the mean distance to it. */
export function circleOf(ring: Position[]): { center: Position; radiusMeters: number } {
  const points = ring.slice(0, -1);
  const center: Position = [points.reduce((s, p) => s + p[0], 0) / points.length, points.reduce((s, p) => s + p[1], 0) / points.length];
  const meters = (a: Position, b: Position) => {
    const toRad = Math.PI / 180;
    const dLat = (b[1] - a[1]) * toRad;
    const dLng = (b[0] - a[0]) * toRad;
    const h = Math.sin(dLat / 2) ** 2 + Math.cos(a[1] * toRad) * Math.cos(b[1] * toRad) * Math.sin(dLng / 2) ** 2;
    return 2 * 6371008.8 * Math.asin(Math.sqrt(h));
  };
  return { center, radiusMeters: points.reduce((s, p) => s + meters(center, p), 0) / points.length };
}

/** Polygon or MultiPolygon from martinez output (whose nesting depth says which it is); null when nothing is left. */
function toGeometry(result: Position[][] | Position[][][] | null): Polygon | MultiPolygon | null {
  if (!result || result.length === 0) {
    return null;
  }
  const isPolygon = typeof (result as Position[][])[0][0][0] === 'number';
  const polygons = (isPolygon ? [result] : result) as Position[][][];
  return polygons.length === 1 ? { type: 'Polygon', coordinates: polygons[0] } : { type: 'MultiPolygon', coordinates: polygons };
}

/** A shape Terra Draw can hold: no holes, not a multipolygon. */
function isEditable(geometry: Geometry): boolean {
  return !(geometry.type === 'MultiPolygon' || (geometry.type === 'Polygon' && geometry.coordinates.length > 1));
}

function modeOf(feature: Feature, allowed: DrawModeName[], circleAsPoint: boolean): DrawModeName | null {
  const geometry = feature.geometry;
  const properties = feature.properties ?? {};
  switch (geometry.type) {
    case 'Polygon':
      return allowed.includes('polygon') ? 'polygon' : allowed.includes('rectangle') ? 'rectangle' : null;
    case 'LineString':
      return allowed.includes('linestring') ? 'linestring' : null;
    case 'Point':
      if (circleAsPoint && properties.shape === 'Circle' && Number(properties.radius) > 0 && allowed.includes('circle')) {
        return 'circle';
      }
      if (typeof properties.text === 'string' && properties.text && allowed.includes('text')) {
        return 'text';
      }
      return allowed.includes('point') || allowed.includes('text') ? 'point' : null;
    default:
      return null;
  }
}

export async function startDrawing(map: import('maplibre-gl').Map, options: DrawOptions): Promise<DrawHandle> {
  const [terra, adapterModule, maplibre] = await Promise.all([
    import('terra-draw'),
    import('terra-draw-maplibre-gl-adapter'),
    import('./maplibreLoader').then((m) => m.loadMapLibre()),
  ]);
  const martinez = options.cut ? await import('martinez-polygon-clipping') : null;
  const color = (options.color ?? '#3388ff') as `#${string}`;
  const label = (name: ToolName | 'textPrompt' | 'selectFirst') => options.labels?.[name] ?? TITLES[name];
  const styles = { fillColor: color, outlineColor: color, fillOpacity: 0.1, outlineWidth: 2 };
  const snapping = options.snapping ? { toCoordinate: true, toLine: true } : undefined;
  const circleAsPoint = !!options.circleAsPoint;
  const wantsPoints = options.modes.includes('point') || options.modes.includes('text');

  // Text is a point with a label, so text alone still needs point mode; cut draws with its own polygon mode.
  const terraModes: unknown[] = [];
  const flags: Record<string, unknown> = {};
  const editable = { feature: { draggable: true, coordinates: { midpoints: true, draggable: true, deletable: true, snappable: !!snapping } } };
  for (const mode of options.modes) {
    switch (mode) {
      case 'polygon':
        terraModes.push(new terra.TerraDrawPolygonMode({ styles, snapping } as any));
        flags.polygon = editable;
        break;
      case 'rectangle':
        terraModes.push(new terra.TerraDrawRectangleMode({ styles }));
        flags.rectangle = { feature: { draggable: true, coordinates: { draggable: true, resizable: 'opposite' } } };
        break;
      case 'linestring':
        terraModes.push(new terra.TerraDrawLineStringMode({ styles: { lineStringColor: color, lineStringWidth: 3 }, snapping } as any));
        flags.linestring = editable;
        break;
      case 'circle':
        terraModes.push(new terra.TerraDrawCircleMode({ styles }));
        flags.circle = { feature: { draggable: true, coordinates: { resizable: 'center-fixed' } } };
        break;
      default:
        break;
    }
  }
  if (wantsPoints) {
    // Text points draw small: the label is what the user sees.
    terraModes.push(
      new terra.TerraDrawPointMode({
        styles: {
          pointColor: color,
          pointWidth: ((feature: { properties?: Record<string, unknown> }) => (feature.properties?.text ? 3 : 6)) as any,
          pointOutlineColor: '#ffffff' as `#${string}`,
          pointOutlineWidth: 1,
        },
      }),
    );
    flags.point = { feature: { draggable: true } };
  }
  if (martinez) {
    terraModes.push(new terra.TerraDrawPolygonMode({ modeName: 'cut', styles: { fillColor: '#d9534f', outlineColor: '#d9534f', fillOpacity: 0.2, outlineWidth: 2 }, snapping } as any));
  }

  // While placing text or rotating, Terra Draw must neither select nor drag what is under the pointer.
  terraModes.push(new terra.TerraDrawRenderMode({ modeName: 'rg-idle', styles: {} } as any));

  const draw = new terra.TerraDraw({
    adapter: new adapterModule.TerraDrawMapLibreGLAdapter({ map, coordinatePrecision: COORDINATE_PRECISION }),
    modes: [...terraModes, new terra.TerraDrawSelectMode({ flags } as any)] as any[],
  });

  draw.start();

  // ---- Shapes Terra Draw cannot hold (holes, multipolygons) -------------------------------------------------------
  let statics: Feature[] = [];
  let staticSelected: string | null = null;

  const renderStatics = () => {
    const data: FeatureCollection = {
      type: 'FeatureCollection',
      features: statics.map((feature) => ({ ...feature, properties: { ...(feature.properties ?? {}), __selected: feature.id === staticSelected } })),
    };
    const source = map.getSource(STATIC_SOURCE) as import('maplibre-gl').GeoJSONSource | undefined;
    if (source) {
      source.setData(data);
      return;
    }
    if (statics.length === 0) {
      return;
    }
    map.addSource(STATIC_SOURCE, { type: 'geojson', data });
    map.addLayer({ id: `${STATIC_SOURCE}-fill`, type: 'fill', source: STATIC_SOURCE, paint: { 'fill-color': color, 'fill-opacity': ['case', ['get', '__selected'], 0.3, 0.1] } });
    map.addLayer({
      id: `${STATIC_SOURCE}-line`,
      type: 'line',
      source: STATIC_SOURCE,
      paint: { 'line-color': ['case', ['get', '__selected'], '#f0ad4e', color], 'line-width': ['case', ['get', '__selected'], 3, 2] },
    });
  };

  const selectStatic = (id: string | null) => {
    staticSelected = id;
    renderStatics();
  };

  const onMapClick = (event: import('maplibre-gl').MapMouseEvent) => {
    if (draw.getMode() !== 'select' || !map.getLayer(`${STATIC_SOURCE}-fill`)) {
      return;
    }
    const hit = map.queryRenderedFeatures(event.point, { layers: [`${STATIC_SOURCE}-fill`] })[0];
    if (hit) {
      // Terra Draw's own selection goes, so only one shape is ever selected.
      draw.getSnapshot().filter((feature) => feature.properties?.selected).forEach((feature) => draw.deselectFeature(feature.id as string));
      selectStatic(String(hit.properties?.__id));
    } else if (staticSelected) {
      selectStatic(null);
    }
  };
  map.on('click', onMapClick);

  const addStatic = (feature: Feature) => {
    const id = String(feature.id ?? newId());
    statics.push({ type: 'Feature', id, geometry: feature.geometry, properties: { ...(feature.properties ?? {}), __id: id, mode: 'static' } });
  };

  // ---- Text labels ------------------------------------------------------------------------------------------------
  const labels = new Map<string, import('maplibre-gl').Marker>();
  const renderLabels = () => {
    const seen = new Set<string>();
    for (const feature of draw.getSnapshot()) {
      const text = feature.properties?.text;
      if (feature.geometry.type !== 'Point' || typeof text !== 'string' || !text) {
        continue;
      }
      const id = String(feature.id);
      seen.add(id);
      const at = feature.geometry.coordinates as [number, number];
      let marker = labels.get(id);
      if (!marker) {
        const element = document.createElement('div');
        element.className = 'rg-draw-text';
        element.style.cssText = `font: 600 13px/1.2 sans-serif; color: ${color}; white-space: nowrap; pointer-events: none; text-shadow: 0 0 2px #fff, 0 0 2px #fff, 0 0 3px #fff;`;
        marker = new maplibre.Marker({ element, anchor: 'left', offset: [6, 0] }).setLngLat(at).addTo(map);
        labels.set(id, marker);
      }
      marker.setLngLat(at);
      if (marker.getElement().textContent !== text) {
        marker.getElement().textContent = text;
      }
    }
    labels.forEach((marker, id) => {
      if (!seen.has(id)) {
        marker.remove();
        labels.delete(id);
      }
    });
  };

  // ---- Snapshot and change notification ----------------------------------------------------------------------------
  const snapshot = (): Feature[] => {
    const drawn = draw
      .getSnapshot()
      .filter((feature) => feature.properties?.mode !== 'cut')
      .map((feature): Feature => {
        const properties: Record<string, unknown> = { ...(feature.properties ?? {}) };
        delete properties.selected;
        if (properties.mode === 'point' && typeof properties.text === 'string' && properties.text) {
          properties.mode = 'text';
        }
        if (circleAsPoint && properties.mode === 'circle' && feature.geometry.type === 'Polygon') {
          const { center, radiusMeters } = circleOf(feature.geometry.coordinates[0] as Position[]);
          const radius = Number(properties.radiusKilometers) > 0 ? Number(properties.radiusKilometers) * 1000 : radiusMeters;
          delete properties.radiusKilometers;
          return { type: 'Feature', id: feature.id, geometry: { type: 'Point', coordinates: center }, properties: { ...properties, shape: 'Circle', radius: Math.round(radius * 100) / 100 } };
        }
        return { type: 'Feature', id: feature.id, geometry: feature.geometry as Geometry, properties };
      });
    const kept = statics.map((feature): Feature => {
      const properties = { ...(feature.properties ?? {}) };
      delete properties.__id;
      return { type: 'Feature', id: feature.id, geometry: feature.geometry, properties };
    });
    return [...drawn, ...kept];
  };

  const notify = () => {
    renderLabels();
    renderStatics();
    options.onChange?.(snapshot());
  };

  // Labels follow a text point while it is dragged; a shape removed with the Delete key has no finish event.
  let labelFrame = 0;
  let deleted = false;
  draw.on('change', (_ids: unknown, type: string) => {
    deleted = deleted || type === 'delete';
    if (!labelFrame) {
      labelFrame = requestAnimationFrame(() => {
        labelFrame = 0;
        if (deleted) {
          deleted = false;
          notify();
        } else {
          renderLabels();
        }
      });
    }
  });

  // ---- Shapes the page already has (stored geofence, saved bounds, layer features) --------------------------------
  if (options.initial) {
    const collection: FeatureCollection =
      options.initial.type === 'FeatureCollection' ? options.initial : { type: 'FeatureCollection', features: [{ type: 'Feature', properties: {}, geometry: options.initial }] };

    const features: unknown[] = [];
    for (const feature of collection.features) {
      if (!feature?.geometry) {
        continue;
      }
      if (!isEditable(feature.geometry)) {
        addStatic(feature);
        continue;
      }
      const mode = modeOf(feature, options.modes, circleAsPoint);
      if (!mode) {
        continue;
      }
      const properties = { ...(feature.properties ?? {}) };
      if (mode === 'circle') {
        const radius = Number(properties.radius);
        delete properties.radius;
        features.push({
          type: 'Feature',
          id: newId(),
          geometry: rounded<Polygon>({ type: 'Polygon', coordinates: [circleRing((feature.geometry as any).coordinates, radius)] }),
          properties: { ...properties, mode: 'circle', radiusKilometers: radius / 1000 },
        });
      } else {
        features.push({ type: 'Feature', id: newId(), geometry: rounded(feature.geometry), properties: { ...properties, mode: mode === 'text' ? 'point' : mode } });
      }
    }

    if (features.length > 0) {
      const results = draw.addFeatures(features as any);
      // Anything Terra Draw refuses (self-intersecting, too few points) is kept rather than dropped.
      results.forEach((result, index) => {
        if (!result.valid) {
          addStatic(features[index] as Feature);
        }
      });
    }
    renderLabels();
    renderStatics();
  }

  // ---- Toolbar -------------------------------------------------------------------------------------------------------
  ensureControlStyles();
  const bar = document.createElement('div');
  bar.className = 'maplibregl-ctrl maplibregl-ctrl-group rg-draw-toolbar';
  const buttons = new Map<string, HTMLButtonElement>();
  let pending: 'text' | 'rotate' | null = null;

  const highlight = (name: string) => buttons.forEach((button, key) => button.classList.toggle('active', key === name));

  const select = (mode: DrawModeName | 'select' | 'cut') => {
    pending = null;
    rotateTarget = null;
    map.dragPan.enable();
    map.getCanvas().style.cursor = '';
    draw.setMode(mode);
    highlight(mode);
  };

  const park = (tool: 'text' | 'rotate', cursor: string) => {
    draw.setMode('rg-idle');
    pending = tool;
    map.getCanvas().style.cursor = cursor;
    highlight(tool);
  };

  /** The one selected shape: Terra Draw's, else a kept one. */
  const selected = (): { id: string; geometry: Geometry; isStatic: boolean } | null => {
    const terraSelected = draw.getSnapshot().find((feature) => feature.properties?.selected);
    if (terraSelected) {
      return { id: String(terraSelected.id), geometry: terraSelected.geometry as Geometry, isStatic: false };
    }
    const kept = statics.find((feature) => feature.id === staticSelected);
    return kept ? { id: String(kept.id), geometry: kept.geometry, isStatic: true } : null;
  };

  const addButton = (name: ToolName, onClick: () => void) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.title = label(name);
    button.setAttribute('aria-label', button.title);
    button.innerHTML = `<i class="fa ${ICONS[name]}" aria-hidden="true"></i>`;
    button.addEventListener('click', (event) => {
      event.preventDefault();
      onClick();
    });
    bar.appendChild(button);
    buttons.set(name, button);
  };

  // Text: the next map click places it; with a text point selected, edits that text instead.
  const placeText = (event: import('maplibre-gl').MapMouseEvent) => {
    if (pending !== 'text') {
      return;
    }
    select('select');
    const text = window.prompt(label('textPrompt'), '');
    if (text && text.trim()) {
      draw.addFeatures([{ type: 'Feature', id: newId(), geometry: rounded<Point>({ type: 'Point', coordinates: [event.lngLat.lng, event.lngLat.lat] }), properties: { mode: 'point', text: text.trim() } }] as any);
      notify();
    }
  };
  map.on('click', placeText);

  // Rotate: drag anywhere while the tool is on; the selected shape turns about its centre in screen space.
  let rotateTarget: string | null = null;
  let rotating: { id: string; origin: [number, number]; last: number } | null = null;
  const angleAt = (origin: [number, number], point: { x: number; y: number }) => {
    const center = map.project(origin);
    return (Math.atan2(point.y - center.y, point.x - center.x) * 180) / Math.PI;
  };
  const onRotateStart = (event: import('maplibre-gl').MapMouseEvent) => {
    const target = pending === 'rotate' && rotateTarget ? draw.getSnapshotFeature(rotateTarget) : undefined;
    if (!target) {
      return;
    }
    const coordinates: Position[] = [];
    const walk = (c: unknown): void => {
      if (Array.isArray(c) && typeof c[0] === 'number') coordinates.push(c as Position);
      else if (Array.isArray(c)) c.forEach(walk);
    };
    walk((target.geometry as any).coordinates);
    const origin: [number, number] = [coordinates.reduce((s, p) => s + p[0], 0) / coordinates.length, coordinates.reduce((s, p) => s + p[1], 0) / coordinates.length];
    rotating = { id: String(target.id), origin, last: angleAt(origin, event.point) };
  };
  const onRotateMove = (event: import('maplibre-gl').MapMouseEvent) => {
    if (!rotating) {
      return;
    }
    const angle = angleAt(rotating.origin, event.point);
    draw.transformFeatureGeometry(rotating.id, { type: 'rotate', origin: rotating.origin, projection: 'web-mercator', options: { angle: angle - rotating.last } });
    rotating.last = angle;
  };
  const onRotateEnd = () => {
    if (rotating) {
      rotating = null;
      notify();
    }
  };
  map.on('mousedown', onRotateStart);
  map.on('mousemove', onRotateMove);
  map.on('mouseup', onRotateEnd);

  // Cut: the polygon drawn in cut mode is taken away from the selected polygon.
  let cutTarget: { id: string; geometry: Polygon | MultiPolygon; isStatic: boolean } | null = null;

  for (const mode of options.modes) {
    if (mode === 'text') {
      addButton('text', () => {
        const target = selected();
        const current = target && !target.isStatic ? draw.getSnapshotFeature(target.id) : undefined;
        if (current && typeof current.properties?.text === 'string') {
          const text = window.prompt(label('textPrompt'), current.properties.text as string);
          if (text && text.trim()) {
            draw.updateFeatureProperties(target!.id, { text: text.trim() });
            notify();
          }
          return;
        }
        park('text', 'crosshair');
      });
    } else {
      addButton(mode, () => select(mode));
    }
  }

  if (martinez) {
    addButton('cut', () => {
      const target = selected();
      if (!target || (target.geometry.type !== 'Polygon' && target.geometry.type !== 'MultiPolygon')) {
        window.alert(label('selectFirst'));
        return;
      }
      cutTarget = { id: target.id, geometry: target.geometry, isStatic: target.isStatic };
      select('cut');
    });
  }

  if (options.rotate) {
    addButton('rotate', () => {
      const target = selected();
      if (!target || target.isStatic || target.geometry.type === 'Point') {
        window.alert(label('selectFirst'));
        return;
      }
      park('rotate', 'grab');
      rotateTarget = target.id;
      map.dragPan.disable();
    });
  }

  addButton('select', () => select('select'));
  addButton('delete', () => {
    const ids = draw.getSnapshot().filter((feature) => feature.properties?.selected).map((feature) => feature.id as string);
    if (ids.length > 0) {
      draw.removeFeatures(ids);
    }
    if (staticSelected) {
      statics = statics.filter((feature) => feature.id !== staticSelected);
      staticSelected = null;
    }
    notify();
  });
  addButton('clear', () => {
    draw.clear();
    statics = [];
    staticSelected = null;
    notify();
  });

  draw.on('finish', (id: string | number, context: { mode?: string; action?: string }) => {
    if (context?.mode === 'cut') {
      const cutter = draw.getSnapshotFeature(id);
      draw.removeFeatures([id]);
      const target = cutTarget;
      cutTarget = null;
      select('select');

      if (cutter && target && martinez) {
        const result = toGeometry(martinez.diff(target.geometry.coordinates as any, (cutter.geometry as Polygon).coordinates as any) as any);
        const original = target.isStatic ? statics.find((feature) => feature.id === target.id) : draw.getSnapshotFeature(target.id);
        const properties = { ...((original?.properties as Record<string, unknown>) ?? {}) };
        delete properties.selected;
        delete properties.__id;

        if (target.isStatic) {
          statics = statics.filter((feature) => feature.id !== target.id);
          staticSelected = null;
        } else {
          draw.removeFeatures([target.id]);
        }

        // The original is already gone, so a piece Terra Draw refuses (e.g. self-touching) is kept rather than lost.
        const piece = result && isEditable(result) ? { type: 'Feature', id: newId(), geometry: rounded(result), properties: { ...properties, mode: 'polygon' } } : null;
        const added = !!piece && !!draw.addFeatures([piece] as any)[0]?.valid;
        if (result && !added) {
          addStatic({ type: 'Feature', id: newId(), geometry: result, properties });
        }
      }

      notify();
      return;
    }

    // A newly drawn shape replaces the others when only one is allowed.
    if (options.single && context?.action === 'draw') {
      const others = draw.getSnapshot().filter((feature) => feature.id !== id).map((feature) => feature.id as string);
      if (others.length > 0) {
        draw.removeFeatures(others);
      }
    }

    notify();
  });

  const control = { onAdd: () => bar, onRemove: () => bar.remove() };
  map.addControl(control, 'top-left');
  select('select');

  return {
    setMode: (mode) => select(mode),
    getMode: () => (pending ?? draw.getMode()),
    getFeatures: snapshot,
    clear: () => {
      draw.clear();
      statics = [];
      staticSelected = null;
      notify();
    },
    destroy: () => {
      map.off('click', onMapClick);
      map.off('click', placeText);
      map.off('mousedown', onRotateStart);
      map.off('mousemove', onRotateMove);
      map.off('mouseup', onRotateEnd);
      labels.forEach((marker) => marker.remove());
      labels.clear();
      if (map.getLayer(`${STATIC_SOURCE}-fill`)) map.removeLayer(`${STATIC_SOURCE}-fill`);
      if (map.getLayer(`${STATIC_SOURCE}-line`)) map.removeLayer(`${STATIC_SOURCE}-line`);
      if (map.getSource(STATIC_SOURCE)) map.removeSource(STATIC_SOURCE);
      draw.stop();
      map.removeControl(control);
    },
  };
}
