import type { VizPalette } from './vizTypes';

// Continuous ramps, low → high. Each is readable on both light street tiles and dark/satellite tiles.
const ramps: Record<VizPalette, string[]> = {
  heat: ['#2c7bb6', '#00a6ca', '#00ccbc', '#90eb9d', '#ffff8c', '#f9d057', '#f29e2e', '#e76818', '#d7191c'],
  fire: ['#fff5b1', '#fed976', '#feb24c', '#fd8d3c', '#fc4e2a', '#e31a1c', '#bd0026', '#800026'],
  viridis: ['#440154', '#482878', '#3e4989', '#31688e', '#26828e', '#1f9e89', '#35b779', '#6ece58', '#b5de2b', '#fde725'],
  blues: ['#deebf7', '#c6dbef', '#9ecae1', '#6baed6', '#4292c6', '#2171b5', '#08519c', '#08306b'],
  traffic: ['#1a9850', '#66bd63', '#a6d96a', '#d9ef8b', '#fee08b', '#fdae61', '#f46d43', '#d73027', '#a50026'],
};

export function getRamp(palette: VizPalette): string[] {
  return ramps[palette] ?? ramps.heat;
}

function hexToRgb(hex: string): [number, number, number] {
  const value = hex.replace('#', '');
  const full = value.length === 3 ? value.split('').map((c) => c + c).join('') : value;
  const parsed = Number.parseInt(full, 16);
  return [(parsed >> 16) & 255, (parsed >> 8) & 255, parsed & 255];
}

/** Color at position t (0–1) along the palette, interpolated between its stops. */
export function rampColor(palette: VizPalette, t: number): string {
  const stops = getRamp(palette);
  const clamped = Math.min(Math.max(Number.isFinite(t) ? t : 0, 0), 1);
  const scaled = clamped * (stops.length - 1);
  const index = Math.min(Math.floor(scaled), stops.length - 2);
  const fraction = scaled - index;
  const [r1, g1, b1] = hexToRgb(stops[index]);
  const [r2, g2, b2] = hexToRgb(stops[index + 1]);
  const mix = (a: number, b: number) => Math.round(a + (b - a) * fraction);
  return `rgb(${mix(r1, r2)}, ${mix(g1, g2)}, ${mix(b1, b2)})`;
}

/** 256 RGBA entries for colorizing a heat canvas's alpha channel. */
export function buildHeatGradient(palette: VizPalette): Uint8ClampedArray {
  const canvas = document.createElement('canvas');
  canvas.width = 1;
  canvas.height = 256;
  const context = canvas.getContext('2d');

  if (!context) {
    return new Uint8ClampedArray(1024);
  }

  const gradient = context.createLinearGradient(0, 0, 0, 256);
  const stops = getRamp(palette);
  stops.forEach((color, index) => gradient.addColorStop(index / (stops.length - 1), color));
  context.fillStyle = gradient;
  context.fillRect(0, 0, 1, 256);

  return context.getImageData(0, 0, 1, 256).data;
}

/**
 * Class breaks for a continuous legend: quantiles of the values, so a few extreme cells do not wash the rest of
 * the map into one color. Returns ascending, de-duplicated upper bounds (the last is the maximum).
 */
export function quantileBreaks(values: number[], classes = 5): number[] {
  const sorted = values.filter((v) => Number.isFinite(v)).sort((a, b) => a - b);

  if (sorted.length === 0) {
    return [];
  }

  const breaks: number[] = [];
  for (let i = 1; i <= classes; i += 1) {
    const position = Math.min(sorted.length - 1, Math.ceil((i / classes) * sorted.length) - 1);
    const value = sorted[position];
    if (breaks.length === 0 || value > breaks[breaks.length - 1]) {
      breaks.push(value);
    }
  }

  return breaks;
}

/** Index of the class a value falls in (0-based), given ascending upper bounds. */
export function classIndex(value: number, breaks: number[]): number {
  for (let i = 0; i < breaks.length; i += 1) {
    if (value <= breaks[i]) {
      return i;
    }
  }

  return Math.max(breaks.length - 1, 0);
}

export function classColor(palette: VizPalette, index: number, classCount: number): string {
  return rampColor(palette, classCount <= 1 ? 1 : index / (classCount - 1));
}
