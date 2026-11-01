import type { DashboardLayout, Density, LayoutWidget } from './types';

export const SCHEMA_VERSION = 1;

/** JSON with object keys sorted, so two equal settings objects compare equal regardless of key order. */
export function stableStringify(value: unknown): string {
  if (value === null || typeof value !== 'object') {
    return JSON.stringify(value) ?? 'null';
  }
  if (Array.isArray(value)) {
    return `[${value.map(stableStringify).join(',')}]`;
  }
  const record = value as Record<string, unknown>;
  return `{${Object.keys(record)
    .filter((key) => record[key] !== undefined)
    .sort()
    .map((key) => `${JSON.stringify(key)}:${stableStringify(record[key])}`)
    .join(',')}}`;
}

export function sameSettings(a: Record<string, unknown> | null | undefined, b: Record<string, unknown> | null | undefined): boolean {
  return stableStringify(a ?? {}) === stableStringify(b ?? {});
}

export function newInstanceId(): string {
  const cryptoObject = typeof crypto !== 'undefined' ? crypto : undefined;
  if (cryptoObject && typeof cryptoObject.randomUUID === 'function') {
    return cryptoObject.randomUUID();
  }
  const bytes = new Uint8Array(16);
  if (cryptoObject && typeof cryptoObject.getRandomValues === 'function') {
    cryptoObject.getRandomValues(bytes);
  } else {
    for (let index = 0; index < bytes.length; index += 1) {
      bytes[index] = Math.floor(Math.random() * 256);
    }
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function densityOf(layout: DashboardLayout | null | undefined): Density {
  return layout?.Density === 'compact' ? 'compact' : 'comfortable';
}

/** A layout with every collection present (the server may omit empty ones). */
export function normalizeLayout(layout: DashboardLayout | null | undefined): DashboardLayout {
  return {
    SchemaVersion: layout?.SchemaVersion || SCHEMA_VERSION,
    Density: densityOf(layout),
    Widgets: (layout?.Widgets ?? [])
      .filter((widget) => widget && typeof widget.Id === 'string' && typeof widget.Key === 'string')
      .map((widget) => ({
        Id: widget.Id,
        Key: widget.Key,
        Size: widget.Size,
        Settings: widget.Settings && typeof widget.Settings === 'object' ? { ...widget.Settings } : {},
        Collapsed: !!widget.Collapsed,
      })),
  };
}

export function layoutsEqual(a: DashboardLayout | null | undefined, b: DashboardLayout | null | undefined): boolean {
  return stableStringify(normalizeLayout(a)) === stableStringify(normalizeLayout(b));
}

/** Moves `id` before or after `targetId` in the full list (instances not rendered keep their relative places). */
export function moveWidget(widgets: LayoutWidget[], id: string, targetId: string, before: boolean): LayoutWidget[] {
  if (id === targetId) {
    return widgets;
  }
  const moving = widgets.find((widget) => widget.Id === id);
  if (!moving) {
    return widgets;
  }
  const rest = widgets.filter((widget) => widget.Id !== id);
  const targetIndex = rest.findIndex((widget) => widget.Id === targetId);
  if (targetIndex < 0) {
    return widgets;
  }
  rest.splice(before ? targetIndex : targetIndex + 1, 0, moving);
  return rest;
}

/** One step earlier/later among the rendered instances. */
export function stepWidget(widgets: LayoutWidget[], id: string, delta: -1 | 1, isRendered: (widget: LayoutWidget) => boolean): LayoutWidget[] {
  const rendered = widgets.filter(isRendered);
  const index = rendered.findIndex((widget) => widget.Id === id);
  const neighbour = rendered[index + delta];
  if (index < 0 || !neighbour) {
    return widgets;
  }
  return moveWidget(widgets, id, neighbour.Id, delta < 0);
}
