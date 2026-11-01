import { createContext, useContext, type MutableRefObject } from 'react';
import type { BusStatus, DashboardBus } from './data/dashboardBus';
import { createFormat, t, type Translate, type WidgetFormat } from './i18n';
import type { Bootstrap, CategoryInfo, DashboardMode, Density, LayoutWidget, MapConfig, SizeToken } from './types';
import type { WidgetAdpState } from './widgets/types';

export interface ToastAction {
  label: string;
  run: () => void;
}

/** What a widget frame can ask the dashboard to do. A stable object: safe in effect dependencies. */
export interface FrameActions {
  remove(id: string): void;
  setSize(id: string, size: SizeToken): void;
  /** Keyboard reorder: one step earlier (-1) or later (+1) in reading order. */
  move(id: string, delta: -1 | 1): void;
  openSettings(id: string): void;
  updateSettings(id: string, patch: Record<string, unknown>): void;
  toast(text: string, action?: ToastAction): void;
  announce(text: string): void;
}

export interface DashboardRuntime {
  mode: DashboardMode;
  editing: boolean;
  wall: boolean;
  density: Density;
  rtl: boolean;
  revealed: boolean;
  adp: WidgetAdpState;
  bus: DashboardBus | null;
  busStatus: BusStatus;
  bootstrap: Bootstrap;
  categories: Map<string, CategoryInfo>;
  format: WidgetFormat;
  boardId: string | null;
  /** The last layout the server stored, by instance id: an instance that matches it reads WidgetData, else WidgetPreview. */
  stored: Map<string, LayoutWidget>;
  actions: FrameActions;
  /** The instance id to focus after the next render (keyboard reorder moves the DOM node, which drops focus). */
  pendingFocus: MutableRefObject<string | null>;
  /** A just-added instance to highlight. */
  flashId: string | null;
}

export const DashboardRuntimeContext = createContext<DashboardRuntime | null>(null);

export function useDashboardRuntime(): DashboardRuntime {
  const runtime = useContext(DashboardRuntimeContext);
  if (!runtime) {
    throw new Error('Dashboard frames must render inside rg-dashboard.');
  }
  return runtime;
}

/** What primitives read from the widget they render in (ProtectedText needs the reveal flow, MiniMap the tiles). */
export interface PrimitiveRuntime {
  adp: WidgetAdpState;
  map: MapConfig | null;
  t: Translate;
  format: WidgetFormat;
  rtl: boolean;
}

const inertAdp: WidgetAdpState = { enforced: false, revealed: false, canReveal: false, requestReveal: () => undefined };

export const PrimitiveRuntimeContext = createContext<PrimitiveRuntime>({
  adp: inertAdp,
  map: null,
  t,
  format: createFormat(undefined),
  rtl: false,
});

export function usePrimitiveRuntime(): PrimitiveRuntime {
  return useContext(PrimitiveRuntimeContext);
}
