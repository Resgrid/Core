import type { VizAggregate, VizPoint } from './vizTypes';

export interface HexCell {
  key: string;
  /** Polygon ring as [lat, lon] pairs. */
  ring: [number, number][];
  centerLat: number;
  centerLon: number;
  count: number;
  /** Aggregated metric (equals count for the count aggregate); null when no row in the cell had a value. */
  value: number | null;
  /** Rows per category index, for dominant-category coloring. */
  categoryCounts: Map<number, number>;
  dominantCategory: number;
  /** Share of the cell's rows in the dominant category (0–1). */
  dominantShare: number;
}

const METERS_PER_DEGREE_LAT = 110540;
const METERS_PER_DEGREE_LON_AT_EQUATOR = 111320;
const SQRT3 = Math.sqrt(3);

function percentile(sortedValues: number[], p: number): number {
  if (sortedValues.length === 1) {
    return sortedValues[0];
  }

  const rank = (p / 100) * (sortedValues.length - 1);
  const lower = Math.floor(rank);
  const upper = Math.ceil(rank);
  return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (rank - lower);
}

export function aggregate(values: number[], method: VizAggregate, count: number): number | null {
  if (method === 'count') {
    return count;
  }

  if (values.length === 0) {
    return null;
  }

  switch (method) {
    case 'sum':
      return values.reduce((total, v) => total + v, 0);
    case 'mean':
      return values.reduce((total, v) => total + v, 0) / values.length;
    case 'max':
      return values.reduce((highest, v) => (v > highest ? v : highest), values[0]);
    case 'p90':
      return percentile([...values].sort((a, b) => a - b), 90);
    case 'median':
    default:
      return percentile([...values].sort((a, b) => a - b), 50);
  }
}

/**
 * Bins points into a pointy-top hexagon grid whose cells are <paramref name="cellSizeMeters"/> wide. Uses a local
 * equirectangular projection centred on the data, which keeps cells true to size across a department's area.
 */
export function binHexagons(points: VizPoint[], cellSizeMeters: number, method: VizAggregate): HexCell[] {
  if (points.length === 0) {
    return [];
  }

  const referenceLat = points.reduce((total, p) => total + p[0], 0) / points.length;
  const metersPerDegreeLon = METERS_PER_DEGREE_LON_AT_EQUATOR * Math.cos((referenceLat * Math.PI) / 180);
  const size = cellSizeMeters / SQRT3; // centre-to-corner

  const toXY = (lat: number, lon: number) => [lon * metersPerDegreeLon, lat * METERS_PER_DEGREE_LAT] as const;
  const toLatLon = (x: number, y: number): [number, number] => [y / METERS_PER_DEGREE_LAT, x / metersPerDegreeLon];

  const buckets = new Map<string, { q: number; r: number; values: number[]; count: number; categories: Map<number, number> }>();

  for (const point of points) {
    const [x, y] = toXY(point[0], point[1]);
    const fq = ((SQRT3 / 3) * x - y / 3) / size;
    const fr = ((2 / 3) * y) / size;
    // Cube rounding.
    const fs = -fq - fr;
    let q = Math.round(fq);
    let r = Math.round(fr);
    const s = Math.round(fs);
    const dq = Math.abs(q - fq);
    const dr = Math.abs(r - fr);
    const ds = Math.abs(s - fs);
    if (dq > dr && dq > ds) {
      q = -r - s;
    } else if (dr > ds) {
      r = -q - s;
    }

    const key = `${q}:${r}`;
    let bucket = buckets.get(key);
    if (!bucket) {
      bucket = { q, r, values: [], count: 0, categories: new Map() };
      buckets.set(key, bucket);
    }

    bucket.count += 1;
    if (point[3] !== null && point[3] !== undefined && Number.isFinite(point[3])) {
      bucket.values.push(point[3]);
    }
    if (point[2] >= 0) {
      bucket.categories.set(point[2], (bucket.categories.get(point[2]) ?? 0) + 1);
    }
  }

  const cells: HexCell[] = [];
  buckets.forEach((bucket, key) => {
    const centerX = size * (SQRT3 * bucket.q + (SQRT3 / 2) * bucket.r);
    const centerY = size * (1.5 * bucket.r);
    const ring: [number, number][] = [];
    for (let i = 0; i < 6; i += 1) {
      const angle = ((60 * i - 30) * Math.PI) / 180;
      ring.push(toLatLon(centerX + size * Math.cos(angle), centerY + size * Math.sin(angle)));
    }

    let dominantCategory = -1;
    let dominantCount = 0;
    bucket.categories.forEach((categoryCount, category) => {
      if (categoryCount > dominantCount) {
        dominantCategory = category;
        dominantCount = categoryCount;
      }
    });

    const [centerLat, centerLon] = toLatLon(centerX, centerY);
    cells.push({
      key,
      ring,
      centerLat,
      centerLon,
      count: bucket.count,
      value: aggregate(bucket.values, method, bucket.count),
      categoryCounts: bucket.categories,
      dominantCategory,
      dominantShare: bucket.count > 0 ? dominantCount / bucket.count : 0,
    });
  });

  return cells;
}
