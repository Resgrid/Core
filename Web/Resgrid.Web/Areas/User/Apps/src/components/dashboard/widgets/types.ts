import type { ComponentType, LazyExoticComponent } from 'react';
import type { ApiQuery } from '../../../runtime/api';
import type { SubmitOptions, SubmitResult } from '../api';
import type { Translate, WidgetFormat } from '../i18n';
import type {
  CategoryInfo,
  DashboardMode,
  Density,
  HeaderInfo,
  LayoutWidget,
  MapConfig,
  SettingOption,
  SizeToken,
  WidgetDefinition,
} from '../types';

// The widget component contract (home-dashboard-implementation-contract.md §4). A widget is
//   export default function XWidget(props: WidgetProps<XWidgetData>) { … }
// registered with one line in widgets/<category>/index.ts. The frame (title, menu, loading, error, empty, stale,
// revealed badge) is not the widget's job; build the body from ../primitives.

export type { ApiQuery, LayoutWidget, SizeToken, SubmitOptions, SubmitResult, Translate, WidgetDefinition, WidgetFormat };

export interface WidgetAdpState {
  /** The department enforces Advanced Data Protection. */
  enforced: boolean;
  /** The page holds a live reveal grant; DashboardProtectedText values arrive as plain text. */
  revealed: boolean;
  /** The step-up flow is available here (host page wired, not wall display, viewer holds a covering permission). */
  canReveal: boolean;
  /** Opens the page's step-up (TOTP, passkey, Responder, SSO). No-op when canReveal is false. */
  requestReveal(): void;
}

export interface WidgetBootstrap {
  departmentId: number;
  /** Department map tiles (MiniMap reads these itself). */
  map: MapConfig | null;
  /** Department option lists by source name (groups, stations, unitTypes, …). */
  options: Record<string, SettingOption[]>;
  header: HeaderInfo | null;
  categories: CategoryInfo[];
}

export interface WidgetContext {
  /** This instance's id in the layout. */
  instanceId: string;
  mode: DashboardMode;
  /** The dashboard is in Customize mode (bodies are covered and inert). */
  editing: boolean;
  density: Density;
  /** Wall display is on (larger type, never revealed). */
  wall: boolean;
  /** The page reads right to left. */
  rtl: boolean;
  /** Re-fetch this widget's data now. While ADP-revealed, protected widgets refresh at most every 120 s. */
  refresh(): void;
  /** Open this widget's settings dialog (no-op when the definition has no settings). */
  openSettings(): void;
  /**
   * Persist a settings patch for this instance (debounced save, e.g. a sticky note's text). Pass { refetch: false } for
   * settings that do not change the server's data, so typing does not refetch.
   */
  updateSettings(patch: Record<string, unknown>, options?: { refetch?: boolean }): void;
  /** Same-origin JSON POST with the anti-forgery header (and the ADP grant when revealed). Rejects on HTTP errors. */
  post<TResponse = unknown>(url: string, body: unknown): Promise<TResponse>;
  /**
   * Calls an existing MVC action that binds from the query string or form and replies with 204, a redirect to HTML or
   * JSON, e.g. submit('/User/Home/SetCustomAction', { query: { actionType: 2 }, form: { note } }) or
   * submit('/User/Dispatch/DispatchCallNow', { form: { callId }, ajax: true }). Same origin, anti-forgery header,
   * redirects not followed: 2xx, 3xx and an unfollowed redirect are ok. `json` is set only for a JSON response. Never
   * throws on the body; a network failure resolves { ok: false, status: 0 }. Refresh afterwards with ctx.refresh().
   */
  submit(url: string, options?: SubmitOptions): Promise<SubmitResult>;
  /**
   * The v4 API through the web BFF (runtime/api.ts apiFetchJson, as rg-map and Admin Assist use): `path` is relative to
   * /api/web-bff/, e.g. api('api/v4/UnitStatusAlerts/Acknowledge', { method: 'POST', headers: { 'Content-Type':
   * 'application/json' }, body: JSON.stringify(input) }). Cookie auth, anti-forgery on writes, the ADP grant when the
   * page holds one. Only BFF-allowed prefixes work (api/v4/UnitStatusAlerts/, api/v4/Routes/AcknowledgeDeviation/).
   * Rejects with ApiError (.status) on HTTP errors; resolves the parsed JSON ({} for an empty body).
   */
  api<TResponse = unknown>(path: string, init?: RequestInit, query?: ApiQuery): Promise<TResponse>;
  /** The count/label shown next to the title; null hides it. Prefer the useWidgetBadge hook. */
  setBadge(value: number | string | null): void;
  /** A short message in the dashboard's toast. */
  toast(text: string): void;
  /** A polite screen-reader announcement (use sparingly: new calls, explicit actions). */
  announce(text: string): void;
  adp: WidgetAdpState;
  bootstrap: WidgetBootstrap;
  /** Numbers, dates and ages in the UI culture and the department's time zone. */
  format: WidgetFormat;
}

export interface WidgetProps<TData = unknown> {
  /** The provider's DTO (PascalCase), never null: an empty payload renders the frame's empty state instead. */
  data: TData;
  instance: LayoutWidget;
  definition: WidgetDefinition;
  /** The size the frame renders at (allowed by the definition). */
  size: SizeToken;
  /** t('W.<key>.<Name>', …args) — DashboardWidgets strings; chrome strings are not for widgets. */
  t: Translate;
  ctx: WidgetContext;
}

export type WidgetComponent<TData = unknown> = ComponentType<WidgetProps<TData>>;

// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type WidgetRegistry = Record<string, LazyExoticComponent<ComponentType<WidgetProps<any>>>>;
