import L from 'leaflet';

// Canvas overlays for the visualizer: a heat surface and a dot layer. Both redraw for the current view only, so a
// layer of tens of thousands of rows stays responsive where one Leaflet vector per row would not.

export interface WeightedPoint {
  lat: number;
  lon: number;
  weight: number;
}

export interface HeatLayerOptions {
  radius: number;
  intensity: number;
  opacity: number;
  /** 256 RGBA entries (see buildHeatGradient). */
  gradient: Uint8ClampedArray;
}

export interface DotPoint {
  lat: number;
  lon: number;
  color: string;
  radius: number;
  /** Index the click callback reports. */
  index: number;
}

abstract class ViewCanvasLayer extends L.Layer {
  protected canvas: HTMLCanvasElement | null = null;
  protected leafletMap: L.Map | null = null;
  /** Map pane the canvas is placed in; panes keep the visualizer's layers in definition order. */
  public paneName = 'overlayPane';
  private readonly redrawHandler = () => this.redraw();
  private readonly hideHandler = () => {
    if (this.canvas) {
      this.canvas.style.visibility = 'hidden';
    }
  };

  public onAdd(map: L.Map): this {
    this.leafletMap = map;
    const canvas = L.DomUtil.create('canvas', 'rg-viz__canvas') as HTMLCanvasElement;
    canvas.style.position = 'absolute';
    canvas.style.pointerEvents = 'none';
    (map.getPane(this.paneName) ?? map.getPanes().overlayPane).appendChild(canvas);
    this.canvas = canvas;

    map.on('moveend zoomend resize viewreset', this.redrawHandler);
    map.on('zoomstart', this.hideHandler);
    this.redraw();
    return this;
  }

  public onRemove(map: L.Map): this {
    map.off('moveend zoomend resize viewreset', this.redrawHandler);
    map.off('zoomstart', this.hideHandler);
    this.canvas?.remove();
    this.canvas = null;
    this.leafletMap = null;
    return this;
  }

  public redraw(): void {
    const map = this.leafletMap;
    const canvas = this.canvas;
    if (!map || !canvas) {
      return;
    }

    const size = map.getSize();
    if (size.x === 0 || size.y === 0) {
      return;
    }

    canvas.width = size.x;
    canvas.height = size.y;
    canvas.style.width = `${size.x}px`;
    canvas.style.height = `${size.y}px`;
    L.DomUtil.setPosition(canvas, map.containerPointToLayerPoint([0, 0]));
    canvas.style.visibility = 'visible';

    const context = canvas.getContext('2d');
    if (!context) {
      return;
    }

    context.clearRect(0, 0, size.x, size.y);
    this.draw(context, map, size);
  }

  protected abstract draw(context: CanvasRenderingContext2D, map: L.Map, size: L.Point): void;
}

export class HeatCanvasLayer extends ViewCanvasLayer {
  private points: WeightedPoint[];
  private heatOptions: HeatLayerOptions;
  private brush: HTMLCanvasElement | null = null;
  private brushRadius = 0;

  constructor(points: WeightedPoint[], options: HeatLayerOptions) {
    super();
    this.points = points;
    this.heatOptions = options;
  }

  private getBrush(radius: number): HTMLCanvasElement {
    if (this.brush && this.brushRadius === radius) {
      return this.brush;
    }

    const blur = Math.round(radius * 0.7);
    const core = Math.max(radius - blur / 2, 1);
    const outer = core + blur;
    const brush = document.createElement('canvas');
    brush.width = brush.height = outer * 2;
    const context = brush.getContext('2d');
    if (context) {
      // Draw the circle off-canvas and keep only its blurred shadow: a soft-edged stamp.
      context.shadowOffsetX = context.shadowOffsetY = outer * 2;
      context.shadowBlur = blur;
      context.shadowColor = 'black';
      context.beginPath();
      context.arc(-outer, -outer, core, 0, Math.PI * 2, true);
      context.closePath();
      context.fill();
    }

    this.brush = brush;
    this.brushRadius = radius;
    return brush;
  }

  protected draw(context: CanvasRenderingContext2D, map: L.Map, size: L.Point): void {
    const { radius, intensity, opacity, gradient } = this.heatOptions;
    const brush = this.getBrush(radius);
    const half = brush.width / 2;
    const cellSize = Math.max(Math.round(radius / 2), 2);
    const columns = Math.ceil((size.x + half * 2) / cellSize) + 1;
    const cells = new Map<number, { x: number; y: number; weight: number }>();

    // Merge nearby points into screen cells first (weighted centroid), so drawing cost follows the view, not the data.
    for (const point of this.points) {
      const p = map.latLngToContainerPoint([point.lat, point.lon]);
      if (p.x < -half || p.y < -half || p.x > size.x + half || p.y > size.y + half) {
        continue;
      }

      const column = Math.floor((p.x + half) / cellSize);
      const row = Math.floor((p.y + half) / cellSize);
      const key = row * columns + column;
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
      return;
    }

    // Scale to the 95th-percentile cell so one very busy address does not leave the rest of the map blank.
    const weights = Array.from(cells.values(), (cell) => cell.weight).sort((a, b) => a - b);
    const reference = weights[Math.min(weights.length - 1, Math.floor(weights.length * 0.95))] || 1;
    const max = reference / Math.max(intensity, 0.01);

    cells.forEach((cell) => {
      context.globalAlpha = Math.min(Math.max(cell.weight / max, 0.06), 1);
      context.drawImage(brush, cell.x - half, cell.y - half);
    });

    context.globalAlpha = 1;
    const image = context.getImageData(0, 0, size.x, size.y);
    const pixels = image.data;
    for (let i = 3; i < pixels.length; i += 4) {
      const alpha = pixels[i];
      if (alpha === 0) {
        continue;
      }

      const offset = alpha * 4;
      pixels[i - 3] = gradient[offset];
      pixels[i - 2] = gradient[offset + 1];
      pixels[i - 1] = gradient[offset + 2];
      pixels[i] = Math.min(255, alpha * 1.6) * opacity;
    }
    context.putImageData(image, 0, 0);
  }
}

export class DotCanvasLayer extends ViewCanvasLayer {
  private points: DotPoint[];
  private opacity: number;
  private onPick: ((index: number, latlng: L.LatLng) => void) | null;
  private drawn: { x: number; y: number; r: number; index: number; lat: number; lon: number }[] = [];
  private readonly clickHandler = (event: L.LeafletMouseEvent) => this.handleClick(event);

  constructor(points: DotPoint[], opacity: number, onPick: ((index: number, latlng: L.LatLng) => void) | null) {
    super();
    this.points = points;
    this.opacity = opacity;
    this.onPick = onPick;
  }

  public onAdd(map: L.Map): this {
    super.onAdd(map);
    map.on('click', this.clickHandler);
    return this;
  }

  public onRemove(map: L.Map): this {
    map.off('click', this.clickHandler);
    return super.onRemove(map);
  }

  protected draw(context: CanvasRenderingContext2D, map: L.Map, size: L.Point): void {
    this.drawn = [];
    context.globalAlpha = this.opacity;
    context.lineWidth = 1;
    context.strokeStyle = 'rgba(17, 24, 39, 0.55)';

    // Larger dots first so small ones stay visible on top.
    const ordered = [...this.points].sort((a, b) => b.radius - a.radius);
    for (const point of ordered) {
      const p = map.latLngToContainerPoint([point.lat, point.lon]);
      if (p.x < -point.radius || p.y < -point.radius || p.x > size.x + point.radius || p.y > size.y + point.radius) {
        continue;
      }

      context.beginPath();
      context.arc(p.x, p.y, point.radius, 0, Math.PI * 2);
      context.fillStyle = point.color;
      context.fill();
      context.stroke();
      this.drawn.push({ x: p.x, y: p.y, r: point.radius, index: point.index, lat: point.lat, lon: point.lon });
    }

    context.globalAlpha = 1;
  }

  private handleClick(event: L.LeafletMouseEvent): void {
    if (!this.onPick || this.drawn.length === 0) {
      return;
    }

    let best: (typeof this.drawn)[number] | null = null;
    let bestDistance = Number.POSITIVE_INFINITY;
    for (const dot of this.drawn) {
      const dx = dot.x - event.containerPoint.x;
      const dy = dot.y - event.containerPoint.y;
      const distance = Math.sqrt(dx * dx + dy * dy);
      if (distance <= dot.r + 4 && distance < bestDistance) {
        best = dot;
        bestDistance = distance;
      }
    }

    if (best) {
      this.onPick(best.index, L.latLng(best.lat, best.lon));
    }
  }
}
