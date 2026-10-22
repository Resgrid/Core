import { binHexagons, type HexCell } from './hexbin';
import { classColor, classIndex, quantileBreaks } from './palettes';
import type { VizDefinition, VizLayer, VizLayerResult, VizPoint, VizResult } from './vizTypes';

// Turns one layer's data into what the map draws and what the legend lists, so the two always agree.

export const NEUTRAL_COLOR = '#2563eb';
const CLASS_COUNT = 5;

export interface DotRender {
  lat: number;
  lon: number;
  color: string;
  radius: number;
  callId: number;
  metric: number | null;
  categoryIndex: number;
}

export interface HexRender {
  cell: HexCell;
  color: string;
  opacity: number;
}

export interface TrackRender {
  id: string;
  label: string;
  color: string;
  coordinates: [number, number][];
  first: number | null;
  last: number | null;
}

export type LegendModel =
  | { kind: 'categories'; items: { label: string; color: string; count: number }[]; note?: 'dominant' }
  | { kind: 'classes'; items: { from: number | null; to: number; color: string }[] }
  | { kind: 'ramp' }
  | { kind: 'none' };

export interface LayerRender {
  layerId: string;
  display: VizLayer['Display'];
  dots: DotRender[];
  heat: { lat: number; lon: number; weight: number }[];
  hexes: HexRender[];
  tracks: TrackRender[];
  legend: LegendModel;
}

function categoryColor(result: VizLayerResult, index: number): string {
  return index >= 0 && index < result.Categories.length ? result.Categories[index].Color : NEUTRAL_COLOR;
}

function hasMetric(layer: VizLayer): boolean {
  return layer.Metric !== 'count';
}

function finiteMetrics(points: VizPoint[]): number[] {
  const values: number[] = [];
  for (const point of points) {
    if (point[3] !== null && Number.isFinite(point[3])) {
      values.push(point[3] as number);
    }
  }
  return values;
}

function classLegend(breaks: number[], layer: VizLayer): LegendModel {
  return {
    kind: 'classes',
    items: breaks.map((upper, index) => ({
      from: index === 0 ? null : breaks[index - 1],
      to: upper,
      color: classColor(layer.Style.Palette, index, breaks.length),
    })),
  };
}

function categoryLegend(result: VizLayerResult, note?: 'dominant'): LegendModel {
  return {
    kind: 'categories',
    items: result.Categories.map((category) => ({ label: category.Label, color: category.Color, count: category.Count })),
    note,
  };
}

function renderPoints(layer: VizLayer, result: VizLayerResult): LayerRender {
  const byCategory = layer.ColorBy !== 'none';
  const metricValues = hasMetric(layer) ? finiteMetrics(result.Points) : [];
  const breaks = !byCategory && metricValues.length > 0 ? quantileBreaks(metricValues, CLASS_COUNT) : [];
  const maxMetric = metricValues.reduce((highest, v) => (v > highest ? v : highest), 0);
  const baseRadius = layer.Style.PointSize;

  const dots = result.Points.map<DotRender>((point) => {
    const metric = point[3];
    let color = NEUTRAL_COLOR;
    if (byCategory) {
      color = categoryColor(result, point[2]);
    } else if (breaks.length > 0 && metric !== null) {
      color = classColor(layer.Style.Palette, classIndex(metric, breaks), breaks.length);
    } else if (breaks.length > 0) {
      color = '#9ca3af';
    }

    // Area, not radius, follows the metric so a location with four times the calls looks four times as big.
    const radius = layer.Style.SizeByMetric && metric !== null && maxMetric > 0
      ? baseRadius + Math.sqrt(metric / maxMetric) * baseRadius * 2.5
      : baseRadius;

    return { lat: point[0], lon: point[1], color, radius, callId: point[4], metric, categoryIndex: point[2] };
  });

  return {
    layerId: layer.Id,
    display: 'points',
    dots,
    heat: [],
    hexes: [],
    tracks: [],
    legend: byCategory ? categoryLegend(result) : breaks.length > 0 ? classLegend(breaks, layer) : { kind: 'none' },
  };
}

function renderHeat(layer: VizLayer, result: VizLayerResult): LayerRender {
  const weighted = hasMetric(layer);
  const heat = result.Points
    .filter((point) => !weighted || (point[3] !== null && Number.isFinite(point[3])))
    .map((point) => ({ lat: point[0], lon: point[1], weight: weighted ? Math.max(point[3] as number, 0) : 1 }));

  // Track heat uses every recorded position as one sample.
  result.Tracks.forEach((track) =>
    track.Coordinates.forEach((coordinate) => heat.push({ lat: coordinate[0], lon: coordinate[1], weight: 1 })),
  );

  return { layerId: layer.Id, display: 'heat', dots: [], heat, hexes: [], tracks: [], legend: { kind: 'ramp' } };
}

function renderHexbins(layer: VizLayer, result: VizLayerResult): LayerRender {
  const points: VizPoint[] = result.Points.length > 0
    ? result.Points
    : result.Tracks.flatMap((track) => track.Coordinates.map<VizPoint>((c) => [c[0], c[1], track.CategoryIndex, null, 0]));
  const cells = binHexagons(points, layer.Style.CellSizeMeters, layer.Aggregate);
  const byCategory = layer.ColorBy !== 'none';

  if (byCategory) {
    const maxCount = cells.reduce((highest, cell) => (cell.count > highest ? cell.count : highest), 1);
    return {
      layerId: layer.Id,
      display: 'hexbins',
      dots: [],
      heat: [],
      tracks: [],
      hexes: cells.map((cell) => ({
        cell,
        color: categoryColor(result, cell.dominantCategory),
        // Busier cells are more solid; a cell with one call should not shout as loudly as one with a hundred.
        opacity: layer.Style.Opacity * (0.35 + 0.65 * Math.sqrt(cell.count / maxCount)),
      })),
      legend: categoryLegend(result, 'dominant'),
    };
  }

  const values = cells.map((cell) => cell.value).filter((value): value is number => value !== null);
  const breaks = quantileBreaks(values, CLASS_COUNT);

  return {
    layerId: layer.Id,
    display: 'hexbins',
    dots: [],
    heat: [],
    tracks: [],
    hexes: cells
      .filter((cell) => cell.value !== null)
      .map((cell) => ({
        cell,
        color: classColor(layer.Style.Palette, classIndex(cell.value as number, breaks), breaks.length),
        opacity: layer.Style.Opacity,
      })),
    legend: breaks.length > 0 ? classLegend(breaks, layer) : { kind: 'none' },
  };
}

function renderTracks(layer: VizLayer, result: VizLayerResult): LayerRender {
  return {
    layerId: layer.Id,
    display: 'tracks',
    dots: [],
    heat: [],
    hexes: [],
    tracks: result.Tracks.map((track) => ({
      id: track.Id,
      label: track.Label,
      color: track.CategoryIndex >= 0 ? categoryColor(result, track.CategoryIndex) : NEUTRAL_COLOR,
      coordinates: track.Coordinates.map((c) => [c[0], c[1]] as [number, number]),
      first: track.Coordinates.length > 0 ? track.Coordinates[0][2] : null,
      last: track.Coordinates.length > 0 ? track.Coordinates[track.Coordinates.length - 1][2] : null,
    })),
    legend: layer.ColorBy !== 'none' ? categoryLegend(result) : { kind: 'none' },
  };
}

export function buildLayerRender(layer: VizLayer, result: VizLayerResult): LayerRender {
  switch (layer.Display) {
    case 'heat':
      return renderHeat(layer, result);
    case 'hexbins':
      return renderHexbins(layer, result);
    case 'tracks':
      return renderTracks(layer, result);
    case 'points':
    default:
      return renderPoints(layer, result);
  }
}

function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
}

function kilometersBetween(a: [number, number], b: [number, number]): number {
  const toRadians = Math.PI / 180;
  const dLat = (b[0] - a[0]) * toRadians;
  const dLon = (b[1] - a[1]) * toRadians;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a[0] * toRadians) * Math.cos(b[0] * toRadians) * Math.sin(dLon / 2) ** 2;
  return 12742 * Math.asin(Math.min(1, Math.sqrt(h)));
}

/**
 * The box to fit the map to, leaving out stray points: a call geocoded to the wrong continent should not zoom the map out
 * to the whole world. Points farther from the median position than four times the median distance (at least 25 km) are
 * ignored, as long as that still keeps most of them.
 */
export function robustBounds(coordinates: [number, number][]): [[number, number], [number, number]] | null {
  if (coordinates.length === 0) {
    return null;
  }

  let kept = coordinates;
  if (coordinates.length >= 5) {
    const center: [number, number] = [median(coordinates.map((c) => c[0])), median(coordinates.map((c) => c[1]))];
    const distances = coordinates.map((c) => kilometersBetween(center, c));
    const limit = Math.max(25, median(distances) * 4);
    const near = coordinates.filter((_, index) => distances[index] <= limit);
    if (near.length >= coordinates.length * 0.7) {
      kept = near;
    }
  }

  // A loop, not Math.min(...array): a track layer can hold more points than a call's argument limit.
  let south = Infinity;
  let west = Infinity;
  let north = -Infinity;
  let east = -Infinity;
  for (const [lat, lon] of kept) {
    south = Math.min(south, lat);
    north = Math.max(north, lat);
    west = Math.min(west, lon);
    east = Math.max(east, lon);
  }
  return [[south, west], [north, east]];
}

/**
 * The data positions of the visible layers, for fitting the map: the rows themselves rather than grid cells or heat, so
 * the outlier filter in robustBounds has every point to weigh.
 */
export function dataCoordinates(definition: VizDefinition, result: VizResult): [number, number][] {
  const coordinates: [number, number][] = [];
  definition.Layers.filter((layer) => layer.Visible).forEach((layer) => {
    const layerResult = result.Layers.find((r) => r.LayerId === layer.Id);
    layerResult?.Points.forEach((point) => coordinates.push([point[0], point[1]]));
    layerResult?.Tracks.forEach((track) => track.Coordinates.forEach((c) => coordinates.push([c[0], c[1]])));
  });
  return coordinates;
}
