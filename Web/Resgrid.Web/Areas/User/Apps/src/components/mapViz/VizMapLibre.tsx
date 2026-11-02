import { useEffect, useRef, useState } from 'react';
import type { Feature, FeatureCollection } from 'geojson';
import { getRamp } from './palettes';
import { dataCoordinates, robustBounds } from './vizRender';
import type { VizMapProps } from './VizMap';

type MapLibreModule = typeof import('maplibre-gl');
type GlMap = import('maplibre-gl').Map;

/**
 * The visualizer map on MapLibre GL (maplibre-mapping-routing-navigation-plan.md §22.2, §22.8), used when the department
 * has Maps.MapLibreWeb on. Heat follows the approved tuning: today's per-view cell merge (radius / 2 px), 95th-percentile
 * scaling and [0.06, 1] clamp run in JavaScript on every move and feed MapLibre's heatmap layer as weights; the brush's
 * gentle saturation is a 1 - exp(-k·density) curve in heatmap-color (saturation 1.2) and the kernel reaches half strength
 * at 0.8 × the brush core. Dots, hexbins, tracks, station rings and the focused call are GL layers and markers.
 */

const GAUSS_PEAK = 0.3989422804014327;
const DENSITY_RANGE = 4;
const SATURATION = 1.2;
const CORE_FIT = 0.8;

function hexToRgb(hex: string): [number, number, number] {
  const parsed = Number.parseInt(hex.replace('#', ''), 16);
  return [(parsed >> 16) & 255, (parsed >> 8) & 255, parsed & 255];
}

function rampAt(stops: string[], t: number): [number, number, number] {
  const scaled = Math.min(Math.max(t, 0), 1) * (stops.length - 1);
  const index = Math.min(Math.floor(scaled), stops.length - 2);
  const fraction = scaled - index;
  const a = hexToRgb(stops[index]);
  const b = hexToRgb(stops[index + 1]);
  return a.map((value, i) => Math.round(value + (b[i] - value) * fraction)) as [number, number, number];
}

function heatmapColor(stops: string[]): unknown[] {
  const expression: unknown[] = ['interpolate', ['linear'], ['heatmap-density'], 0, 'rgba(0,0,0,0)'];
  const steps = 40;
  for (let i = 1; i <= steps; i += 1) {
    const density = i / steps;
    const coverage = 1 - Math.exp(-SATURATION * DENSITY_RANGE * density);
    const [r, g, b] = rampAt(stops, coverage);
    expression.push(density, `rgba(${r},${g},${b},${Math.min(1, coverage * 1.6).toFixed(3)})`);
  }
  return expression;
}

function brushGeometry(radius: number) {
  const blur = Math.round(radius * 0.7);
  const core = Math.max(radius - blur / 2, 1);
  return { core, outer: core + blur };
}

/** Today's cell merge, 95th-percentile reference and clamp, for the current view. */
function viewCells(map: GlMap, points: { lat: number; lon: number; weight: number }[], radius: number, intensity: number): FeatureCollection {
  const canvas = map.getCanvas();
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  const half = brushGeometry(radius).outer;
  const cellSize = Math.max(Math.round(radius / 2), 2);
  const columns = Math.ceil((width + half * 2) / cellSize) + 1;
  const cells = new Map<number, { x: number; y: number; weight: number }>();

  for (const point of points) {
    const p = map.project([point.lon, point.lat]);
    if (p.x < -half || p.y < -half || p.x > width + half || p.y > height + half) {
      continue;
    }

    const key = Math.floor((p.y + half) / cellSize) * columns + Math.floor((p.x + half) / cellSize);
    const cell = cells.get(key);
    if (cell) {
      const total = cell.weight + point.weight;
      if (total > 0) {
        cell.x = (cell.x * cell.weight + p.x * point.weight) / total;
        cell.y = (cell.y * cell.weight + p.y * point.weight) / total;
      }
      cell.weight = total;
    } else {
      cells.set(key, { x: p.x, y: p.y, weight: point.weight });
    }
  }

  if (cells.size === 0) {
    return { type: 'FeatureCollection', features: [] };
  }

  const weights = Array.from(cells.values(), (cell) => cell.weight).sort((a, b) => a - b);
  const reference = weights[Math.min(weights.length - 1, Math.floor(weights.length * 0.95))] || 1;
  const max = reference / Math.max(intensity, 0.01);

  return {
    type: 'FeatureCollection',
    features: Array.from(cells.values(), (cell) => {
      const lngLat = map.unproject([cell.x, cell.y]);
      return {
        type: 'Feature' as const,
        properties: { a: Math.min(Math.max(cell.weight / max, 0.06), 1) },
        geometry: { type: 'Point' as const, coordinates: [lngLat.lng, lngLat.lat] },
      };
    }),
  };
}

function ringLine(lat: number, lon: number, meters: number): [number, number][] {
  const coordinates: [number, number][] = [];
  for (let i = 0; i <= 96; i += 1) {
    const bearing = (i / 96) * 2 * Math.PI;
    const dLat = (meters * Math.cos(bearing)) / 111320;
    const dLon = (meters * Math.sin(bearing)) / (111320 * Math.cos((lat * Math.PI) / 180));
    coordinates.push([lon + dLon, lat + dLat]);
  }
  return coordinates;
}

function padBounds(box: [[number, number], [number, number]], fraction: number): [[number, number], [number, number]] {
  const dLat = (box[1][0] - box[0][0]) * fraction;
  const dLon = (box[1][1] - box[0][1]) * fraction;
  return [
    [box[0][0] - dLat, box[0][1] - dLon],
    [box[1][0] + dLat, box[1][1] + dLon],
  ];
}

export default function VizMapLibre(props: VizMapProps & { styleUrl: string }) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<GlMap | null>(null);
  const libRef = useRef<MapLibreModule | null>(null);
  const [mapVersion, setMapVersion] = useState(0);
  const layerIdsRef = useRef<string[]>([]);
  const cleanupsRef = useRef<(() => void)[]>([]);
  const stationArtifactsRef = useRef<{ layers: string[]; markers: import('maplibre-gl').Marker[] }>({ layers: [], markers: [] });
  const lastFitKeyRef = useRef('');
  const propsRef = useRef(props);
  propsRef.current = props;

  useEffect(() => {
    let cancelled = false;
    let map: GlMap | null = null;
    let observer: ResizeObserver | null = null;

    void (async () => {
      const [maplibre] = await Promise.all([import('../../maps/maplibreLoader').then((m) => m.loadMapLibre()), import('maplibre-gl/dist/maplibre-gl.css')]);
      if (cancelled || !containerRef.current) {
        return;
      }

      libRef.current = maplibre;
      map = new maplibre.Map({
        container: containerRef.current,
        style: props.styleUrl,
        center: [-98.35, 39.5],
        zoom: 3,
        fadeDuration: document.hidden ? 0 : 300,
        attributionControl: { compact: true, customAttribution: props.attribution || undefined },
      });
      map.addControl(new maplibre.NavigationControl({ showCompass: false }), 'top-left');
      map.once('load', () => {
        if (cancelled) {
          return;
        }

        mapRef.current = map;
        lastFitKeyRef.current = '';
        setMapVersion((version) => version + 1);
      });

      observer = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(() => map?.resize()) : null;
      observer?.observe(containerRef.current);
    })();

    return () => {
      cancelled = true;
      observer?.disconnect();
      cleanupsRef.current.forEach((cleanup) => cleanup());
      cleanupsRef.current = [];
      map?.remove();
      mapRef.current = null;
      layerIdsRef.current = [];
    };
  }, [props.styleUrl, props.attribution]);

  // Data layers, bottom to top in definition order.
  useEffect(() => {
    const map = mapRef.current;
    const maplibre = libRef.current;
    if (!map || !maplibre) {
      return;
    }

    cleanupsRef.current.forEach((cleanup) => cleanup());
    cleanupsRef.current = [];
    for (const id of layerIdsRef.current) {
      if (map.getLayer(id)) {
        map.removeLayer(id);
      }
      if (map.getSource(id)) {
        map.removeSource(id);
      }
    }
    layerIdsRef.current = [];

    const tooltip = new maplibre.Popup({ closeButton: false, closeOnClick: false, offset: 8 });
    cleanupsRef.current.push(() => tooltip.remove());

    const hover = (layerId: string, text: (feature: Feature) => string) => {
      const move = (event: any) => {
        const feature = event.features?.[0];
        if (feature) {
          map.getCanvas().style.cursor = 'pointer';
          tooltip.setLngLat(event.lngLat).setHTML(text(feature)).addTo(map);
        }
      };
      const leave = () => {
        map.getCanvas().style.cursor = '';
        tooltip.remove();
      };
      map.on('mousemove', layerId, move);
      map.on('mouseleave', layerId, leave);
      cleanupsRef.current.push(() => {
        map.off('mousemove', layerId, move);
        map.off('mouseleave', layerId, leave);
      });
    };

    props.definition.Layers.forEach((layer, index) => {
      const render = props.renders.find((r) => r.layerId === layer.Id);
      if (!render || !layer.Visible) {
        return;
      }

      const id = `rg-viz-${index}`;

      switch (render.display) {
        case 'heat': {
          const radius = layer.Style.Radius;
          map.addSource(id, { type: 'geojson', data: viewCells(map, render.heat, radius, layer.Style.Intensity) });
          map.addLayer({
            id,
            type: 'heatmap',
            source: id,
            paint: {
              'heatmap-weight': ['get', 'a'],
              'heatmap-radius': (brushGeometry(radius).core * CORE_FIT) / Math.sqrt(Math.LN2 / 4.5),
              'heatmap-intensity': 1 / (GAUSS_PEAK * DENSITY_RANGE),
              'heatmap-color': heatmapColor(getRamp(layer.Style.Palette)) as any,
              'heatmap-opacity': layer.Style.Opacity,
            },
          });
          layerIdsRef.current.push(id);

          // The cells depend on the view, as today's canvas layer redraws on every move.
          const refresh = () => (map.getSource(id) as any)?.setData(viewCells(map, render.heat, radius, layer.Style.Intensity));
          map.on('moveend', refresh);
          cleanupsRef.current.push(() => map.off('moveend', refresh));
          break;
        }
        case 'points': {
          map.addSource(id, {
            type: 'geojson',
            data: {
              type: 'FeatureCollection',
              features: render.dots.map((dot, dotIndex) => ({
                type: 'Feature',
                properties: { i: dotIndex, color: dot.color, radius: dot.radius },
                geometry: { type: 'Point', coordinates: [dot.lon, dot.lat] },
              })),
            },
          });
          map.addLayer({
            id,
            type: 'circle',
            source: id,
            paint: {
              'circle-radius': ['get', 'radius'],
              'circle-color': ['get', 'color'],
              'circle-opacity': layer.Style.Opacity,
              'circle-stroke-color': '#ffffff',
              'circle-stroke-width': 0.8,
              'circle-stroke-opacity': layer.Style.Opacity,
            },
          });
          layerIdsRef.current.push(id);

          const click = (event: any) => {
            const feature = event.features?.[0];
            const dot = feature ? render.dots[Number(feature.properties?.i)] : null;
            if (!dot) {
              return;
            }

            const popup = new maplibre.Popup({ maxWidth: '320px' }).setLngLat([dot.lon, dot.lat]).setText('…').addTo(map);
            void propsRef.current.renderPointPopup(layer, dot).then((node) => {
              if (popup.isOpen()) {
                popup.setDOMContent(node);
              }
            });
          };
          map.on('click', id, click);
          cleanupsRef.current.push(() => map.off('click', id, click));
          break;
        }
        case 'hexbins': {
          map.addSource(id, {
            type: 'geojson',
            data: {
              type: 'FeatureCollection',
              features: render.hexes.map((hex, hexIndex) => {
                const ring = hex.cell.ring.map((p: [number, number]) => [p[1], p[0]]);
                ring.push(ring[0]);
                return {
                  type: 'Feature',
                  properties: { i: hexIndex, color: hex.color, opacity: hex.opacity },
                  geometry: { type: 'Polygon', coordinates: [ring] },
                };
              }),
            },
          });
          map.addLayer({
            id,
            type: 'fill',
            source: id,
            paint: { 'fill-color': ['get', 'color'], 'fill-opacity': ['get', 'opacity'], 'fill-outline-color': ['get', 'color'] },
          });
          layerIdsRef.current.push(id);
          hover(id, (feature) => propsRef.current.formatHex(layer, render.hexes[Number(feature.properties?.i)]));
          break;
        }
        case 'tracks': {
          map.addSource(id, {
            type: 'geojson',
            data: {
              type: 'FeatureCollection',
              features: render.tracks.flatMap((track, trackIndex) => {
                const coordinates = track.coordinates.map((p) => [p[1], p[0]]);
                const end = coordinates[coordinates.length - 1];
                const features: Feature[] = [
                  {
                    type: 'Feature',
                    properties: { i: trackIndex, color: track.color, end: 1 },
                    geometry: { type: 'Point', coordinates: end },
                  },
                ];
                if (coordinates.length > 1) {
                  features.unshift({ type: 'Feature', properties: { i: trackIndex, color: track.color }, geometry: { type: 'LineString', coordinates } });
                }
                return features;
              }),
            },
          });
          map.addLayer({
            id,
            type: 'line',
            source: id,
            filter: ['==', '$type', 'LineString'],
            layout: { 'line-join': 'round', 'line-cap': 'round' },
            paint: { 'line-color': ['get', 'color'], 'line-width': 3, 'line-opacity': layer.Style.Opacity },
          });
          map.addLayer({
            id: `${id}-end`,
            type: 'circle',
            source: id,
            filter: ['==', '$type', 'Point'],
            paint: { 'circle-radius': 4, 'circle-color': ['get', 'color'], 'circle-stroke-color': '#ffffff', 'circle-stroke-width': 1.5 },
          });
          layerIdsRef.current.push(`${id}-end`, id);
          hover(id, (feature) => propsRef.current.formatTrack(layer, render.tracks[Number(feature.properties?.i)]));
          hover(`${id}-end`, (feature) => propsRef.current.formatTrack(layer, render.tracks[Number(feature.properties?.i)]));
          break;
        }
        default:
          break;
      }
    });
  }, [props.definition, props.renders, mapVersion]);

  // Stations and coverage rings.
  useEffect(() => {
    const map = mapRef.current;
    const maplibre = libRef.current;
    if (!map || !maplibre) {
      return;
    }

    const artifacts = stationArtifactsRef.current;
    artifacts.markers.forEach((marker) => marker.remove());
    for (const id of artifacts.layers) {
      if (map.getLayer(id)) {
        map.removeLayer(id);
      }
      if (map.getSource(id)) {
        map.removeSource(id);
      }
    }
    stationArtifactsRef.current = { layers: [], markers: [] };

    const stations = props.result?.Stations ?? [];
    const options = props.definition.Options;
    if (stations.length === 0 || (!options.ShowStations && options.StationRings.length === 0)) {
      return;
    }

    const metersPerUnit = options.DistanceUnit === 'mi' ? 1609.344 : 1000;
    const rings: Feature[] = [];
    stations.forEach((station) => {
      options.StationRings.forEach((ring) => {
        rings.push({ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: ringLine(station.Latitude, station.Longitude, ring * metersPerUnit) } });
      });

      if (options.ShowStations) {
        const image = document.createElement('img');
        image.src = '/images/Mapping/Station.png';
        image.alt = '';
        image.style.cssText = 'width:26px;height:30px;display:block;';
        image.title = `${props.stationLabel}: ${station.Name ?? ''}`;
        stationArtifactsRef.current.markers.push(
          new maplibre.Marker({ element: image, anchor: 'bottom' }).setLngLat([station.Longitude, station.Latitude]).addTo(map),
        );
      }
    });

    if (rings.length > 0) {
      map.addSource('rg-viz-rings', { type: 'geojson', data: { type: 'FeatureCollection', features: rings } });
      map.addLayer({
        id: 'rg-viz-rings',
        type: 'line',
        source: 'rg-viz-rings',
        paint: { 'line-color': '#111827', 'line-width': 1.2, 'line-dasharray': [4, 4] },
      });
      stationArtifactsRef.current.layers.push('rg-viz-rings');
    }
  }, [props.result, props.definition.Options, props.stationLabel, mapVersion]);

  // Fit to the data when a new answer arrives.
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !props.result || lastFitKeyRef.current === props.fitKey) {
      return;
    }

    lastFitKeyRef.current = props.fitKey;
    const coordinates = dataCoordinates(props.definition, props.result);
    if (props.result.Call?.Latitude != null && props.result.Call.Longitude != null) {
      coordinates.push([props.result.Call.Latitude, props.result.Call.Longitude]);
    }

    const fit = (box: [[number, number], [number, number]], maxZoom: number) =>
      map.fitBounds(
        [
          [box[0][1], box[0][0]],
          [box[1][1], box[1][0]],
        ],
        { maxZoom: maxZoom - 1, animate: false, padding: 10 },
      );

    const box = robustBounds(coordinates);
    if (box) {
      fit(padBounds(box as [[number, number], [number, number]], 0.08), 16);
    } else if ((props.result.Stations ?? []).length > 0) {
      const lats = props.result.Stations.map((s) => s.Latitude);
      const lons = props.result.Stations.map((s) => s.Longitude);
      fit(padBounds([[Math.min(...lats), Math.min(...lons)], [Math.max(...lats), Math.max(...lons)]], 0.2), 13);
    } else if (props.result.CenterLatitude != null && props.result.CenterLongitude != null) {
      map.jumpTo({ center: [props.result.CenterLongitude, props.result.CenterLatitude], zoom: 10 });
    }

    // Heat cells depend on the view: re-cut them for the fitted view.
    map.fire('moveend');
  }, [props.fitKey, props.result, props.renders, mapVersion]);

  // Focused call marker.
  useEffect(() => {
    const map = mapRef.current;
    const maplibre = libRef.current;
    const call = props.result?.Call;
    if (!map || !maplibre || !call || call.Latitude == null || call.Longitude == null) {
      return undefined;
    }

    const image = document.createElement('img');
    image.src = '/images/Mapping/Call.png';
    image.alt = '';
    image.style.cssText = 'width:32px;height:37px;display:block;';
    image.title = [call.Number, call.Name].filter(Boolean).join(' · ');
    const marker = new maplibre.Marker({ element: image, anchor: 'bottom' }).setLngLat([call.Longitude, call.Latitude]).addTo(map);

    return () => {
      marker.remove();
    };
  }, [props.result, mapVersion]);

  return <div ref={containerRef} className="rg-viz__map" style={{ height: props.height }} />;
}
