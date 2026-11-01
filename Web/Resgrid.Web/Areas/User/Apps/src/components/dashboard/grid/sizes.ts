import { t } from '../i18n';
import type { SizeToken, WidgetDefinition } from '../types';

// Size presets (plan §5.1). The columns/rows each token spans, and how they clamp at narrower container widths, live in
// dashboard.css (.rg-w[data-size=…] plus the container queries) so one layout renders at every width.
export const SIZE_ORDER: readonly SizeToken[] = ['xs', 's', 'm', 'l', 'xl', 'full'];

export function isSizeToken(value: string | null | undefined): value is SizeToken {
  return !!value && (SIZE_ORDER as readonly string[]).includes(value);
}

/** The definition's allowed sizes in preset order. */
export function allowedSizes(definition: Pick<WidgetDefinition, 'AllowedSizes' | 'DefaultSize'>): SizeToken[] {
  const allowed = SIZE_ORDER.filter((size) => (definition.AllowedSizes ?? []).includes(size));
  if (allowed.length > 0) {
    return allowed;
  }
  return isSizeToken(definition.DefaultSize) ? [definition.DefaultSize] : ['m'];
}

/** The size to render: the stored size when allowed, otherwise the definition's default. */
export function effectiveSize(size: string, definition: Pick<WidgetDefinition, 'AllowedSizes' | 'DefaultSize'>): SizeToken {
  const allowed = allowedSizes(definition);
  if (isSizeToken(size) && allowed.includes(size)) {
    return size;
  }
  return isSizeToken(definition.DefaultSize) && allowed.includes(definition.DefaultSize) ? definition.DefaultSize : allowed[0];
}

/** The next allowed size up (+1) or down (-1), or null at either end. */
export function stepSize(size: SizeToken, definition: Pick<WidgetDefinition, 'AllowedSizes' | 'DefaultSize'>, direction: 1 | -1): SizeToken | null {
  const allowed = allowedSizes(definition);
  const index = allowed.indexOf(size) + direction;
  return index >= 0 && index < allowed.length ? allowed[index] : null;
}

export function sizeLabel(size: string): string {
  return t(`Dash.Size.${size}`);
}
