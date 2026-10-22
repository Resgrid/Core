import { useEffect, useRef, useState } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { DotCanvasLayer, HeatCanvasLayer } from './canvasLayers';
import { buildHeatGradient } from './palettes';
import { dataCoordinates, robustBounds, type DotRender, type HexRender, type LayerRender, type TrackRender } from './vizRender';
import type { VizDefinition, VizLayer, VizResult } from './vizTypes';

export interface VizMapProps {
  tileUrl: string;
  attribution: string;
  definition: VizDefinition;
  result: VizResult | null;
  renders: LayerRender[];
  /** Changes when the data was re-fetched; the map re-fits to the new data then, not on style edits. */
  fitKey: string;
  formatHex: (layer: VizLayer, hex: HexRender) => string;
  formatTrack: (layer: VizLayer, track: TrackRender) => string;
  /** Fills a popup for a clicked point; resolves to the node to show. */
  renderPointPopup: (layer: VizLayer, dot: DotRender) => Promise<HTMLElement>;
  stationLabel: string;
  height: string;
}

const STATION_ICON = L.icon({ iconUrl: '/images/Mapping/Station.png', iconSize: [26, 30], iconAnchor: [13, 30] });
const CALL_ICON = L.icon({ iconUrl: '/images/Mapping/Call.png', iconSize: [32, 37], iconAnchor: [16, 37] });

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string);
}

export default function VizMap(props: VizMapProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const mapRef = useRef<L.Map | null>(null);
  // One canvas renderer per layer pane, reused across redraws: a new renderer per redraw would leave its canvas behind.
  const renderersRef = useRef<Map<string, L.Canvas>>(new Map());
  // Bumped when the map is (re)created, so layers, stations and the fit are applied to the new map.
  const [mapVersion, setMapVersion] = useState(0);
  const dataLayersRef = useRef<L.Layer[]>([]);
  const stationLayerRef = useRef<L.LayerGroup | null>(null);
  const lastFitKeyRef = useRef<string>('');
  const propsRef = useRef(props);
  propsRef.current = props;

  // Map and base tiles.
  useEffect(() => {
    if (!containerRef.current) {
      return undefined;
    }

    const map = L.map(containerRef.current, { zoomControl: true, preferCanvas: true, worldCopyJump: true });
    L.tileLayer(props.tileUrl, { attribution: props.attribution, maxZoom: 20 }).addTo(map);
    map.setView([39.5, -98.35], 4);
    mapRef.current = map;
    renderersRef.current = new Map();
    lastFitKeyRef.current = '';
    setMapVersion((version) => version + 1);

    const stationPane = map.createPane('rg-viz-stations');
    stationPane.style.zIndex = '640';

    const resizeObserver = typeof ResizeObserver !== 'undefined'
      ? new ResizeObserver(() => map.invalidateSize())
      : null;
    resizeObserver?.observe(containerRef.current);

    return () => {
      resizeObserver?.disconnect();
      map.remove();
      mapRef.current = null;
      renderersRef.current = new Map();
      dataLayersRef.current = [];
      stationLayerRef.current = null;
    };
  }, [props.tileUrl, props.attribution]);

  // Data layers, bottom to top in definition order.
  useEffect(() => {
    const map = mapRef.current;
    if (!map) {
      return;
    }

    dataLayersRef.current.forEach((layer) => layer.remove());
    dataLayersRef.current = [];

    props.definition.Layers.forEach((layer, index) => {
      const render = props.renders.find((r) => r.layerId === layer.Id);
      if (!render || !layer.Visible) {
        return;
      }

      const paneName = `rg-viz-layer-${index}`;
      const pane = map.getPane(paneName) ?? map.createPane(paneName);
      pane.style.zIndex = String(410 + index);
      let renderer = renderersRef.current.get(paneName);
      if (!renderer) {
        renderer = L.canvas({ pane: paneName, padding: 0.3 });
        renderersRef.current.set(paneName, renderer);
      }

      switch (render.display) {
        case 'heat': {
          const heat = new HeatCanvasLayer(render.heat, {
            radius: layer.Style.Radius,
            intensity: layer.Style.Intensity,
            opacity: layer.Style.Opacity,
            gradient: buildHeatGradient(layer.Style.Palette),
          });
          heat.paneName = paneName;
          heat.addTo(map);
          dataLayersRef.current.push(heat);
          break;
        }
        case 'points': {
          const dots = new DotCanvasLayer(
            render.dots.map((dot, dotIndex) => ({ lat: dot.lat, lon: dot.lon, color: dot.color, radius: dot.radius, index: dotIndex })),
            layer.Style.Opacity,
            (dotIndex, latlng) => {
              const dot = render.dots[dotIndex];
              if (!dot) {
                return;
              }
              const popup = L.popup({ maxWidth: 320 }).setLatLng(latlng).setContent('…').openOn(map);
              void propsRef.current.renderPointPopup(layer, dot).then((node) => {
                if (popup.isOpen()) {
                  popup.setContent(node);
                }
              });
            },
          );
          dots.paneName = paneName;
          dots.addTo(map);
          dataLayersRef.current.push(dots);
          break;
        }
        case 'hexbins': {
          const group = L.layerGroup();
          render.hexes.forEach((hex) => {
            L.polygon(hex.cell.ring, {
              renderer,
              color: hex.color,
              weight: 0.6,
              opacity: Math.min(1, hex.opacity + 0.15),
              fillColor: hex.color,
              fillOpacity: hex.opacity,
            })
              .bindTooltip(() => propsRef.current.formatHex(layer, hex), { sticky: true })
              .addTo(group);
          });
          group.addTo(map);
          dataLayersRef.current.push(group);
          break;
        }
        case 'tracks': {
          const group = L.layerGroup();
          render.tracks.forEach((track) => {
            if (track.coordinates.length === 1) {
              L.circleMarker(track.coordinates[0], { renderer, radius: 4, color: track.color, fillOpacity: layer.Style.Opacity })
                .bindTooltip(() => propsRef.current.formatTrack(layer, track), { sticky: true })
                .addTo(group);
              return;
            }

            L.polyline(track.coordinates, { renderer, color: track.color, weight: 3, opacity: layer.Style.Opacity })
              .bindTooltip(() => propsRef.current.formatTrack(layer, track), { sticky: true })
              .addTo(group);
            // Where the track ends: the last known position in the window.
            L.circleMarker(track.coordinates[track.coordinates.length - 1], {
              renderer,
              radius: 4,
              color: '#ffffff',
              weight: 1.5,
              fillColor: track.color,
              fillOpacity: 1,
            }).addTo(group);
          });
          group.addTo(map);
          dataLayersRef.current.push(group);
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
    if (!map) {
      return;
    }

    stationLayerRef.current?.remove();
    stationLayerRef.current = null;

    const stations = props.result?.Stations ?? [];
    const options = props.definition.Options;
    if (stations.length === 0 || (!options.ShowStations && options.StationRings.length === 0)) {
      return;
    }

    const metersPerUnit = options.DistanceUnit === 'mi' ? 1609.344 : 1000;
    const group = L.layerGroup();
    stations.forEach((station) => {
      options.StationRings.forEach((ring) => {
        L.circle([station.Latitude, station.Longitude], {
          pane: 'rg-viz-stations',
          radius: ring * metersPerUnit,
          color: '#111827',
          weight: 1.2,
          dashArray: '6 6',
          fill: false,
          interactive: false,
        }).addTo(group);
      });

      if (options.ShowStations) {
        L.marker([station.Latitude, station.Longitude], { icon: STATION_ICON, pane: 'rg-viz-stations', keyboard: false })
          .bindTooltip(`${escapeHtml(props.stationLabel)}: ${escapeHtml(station.Name ?? '')}`)
          .addTo(group);
      }
    });
    group.addTo(map);
    stationLayerRef.current = group;
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

    const box = robustBounds(coordinates);
    if (box) {
      map.fitBounds(L.latLngBounds(box).pad(0.08), { maxZoom: 16, animate: false });
    } else if ((props.result.Stations ?? []).length > 0) {
      map.fitBounds(L.latLngBounds(props.result.Stations.map((s) => [s.Latitude, s.Longitude] as [number, number])).pad(0.2), { maxZoom: 13 });
    } else if (props.result.CenterLatitude != null && props.result.CenterLongitude != null) {
      map.setView([props.result.CenterLatitude, props.result.CenterLongitude], 11);
    }
  }, [props.fitKey, props.result, props.renders, mapVersion]);

  // Focused call marker.
  useEffect(() => {
    const map = mapRef.current;
    const call = props.result?.Call;
    if (!map || !call || call.Latitude == null || call.Longitude == null) {
      return undefined;
    }

    const marker = L.marker([call.Latitude, call.Longitude], {
      icon: CALL_ICON,
      zIndexOffset: 1000,
    })
      .bindTooltip(escapeHtml([call.Number, call.Name].filter(Boolean).join(' · ')))
      .addTo(map);

    return () => {
      marker.remove();
    };
  }, [props.result, mapVersion]);

  return <div ref={containerRef} className="rg-viz__map" style={{ height: props.height }} />;
}
