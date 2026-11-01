import { useEffect } from 'react';
import type { WidgetContext } from './types';

/**
 * Shows `value` as the frame's header badge (a count next to the title) while the widget is mounted; null or
 * undefined hides it. A DTO property named `Badge` is shown automatically when the widget sets nothing.
 */
export function useWidgetBadge(ctx: WidgetContext, value: number | string | null | undefined): void {
  useEffect(() => {
    ctx.setBadge(value === undefined ? null : value);
  }, [ctx, value]);

  useEffect(() => () => ctx.setBadge(null), [ctx]);
}
