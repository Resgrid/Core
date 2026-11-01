# Dashboard primitives

Every widget body is assembled from these components so seventy widgets look like one product
(home-dashboard-redesign-plan.md §6, contract §4). Import them from `../../primitives`:

```tsx
import { ItemList, StatusChip, ProtectedText } from '../../primitives';
```

Rules that apply to all of them:

- **The frame is not your job.** Title, menu, loading skeleton, error, stale and "Revealed" states come from
  `chrome/WidgetFrame.tsx`. Render only the body. Return `<EmptyState>` when there is nothing to show.
- **Colors that mean something come from the DTO** (department custom state `ButtonColor`/`TextColor`, priority
  colors). Never hard-code status colors. Text always uses the text tokens; never color text by series.
- **Strings** come from `t('W.<key>.<Name>')` (the `t` prop). Numbers, dates and ages come from `ctx.format`
  (UI culture + department time zone), e.g. `ctx.format.time(call.LoggedOnUtc)`, `ctx.format.duration(minutes)`.
- Every class is `rg-` prefixed. If a widget needs a few extra styles, put them in a `.css` file next to the widget,
  prefix every class `rg-<widget>-`, and use the tokens (`var(--rg-heading)`, `var(--rg-muted)`, …).
- `Tone` is `'good' | 'warn' | 'bad' | 'info'`: it adds the matching icon (check / triangle / exclamation / info) in
  an accessible color. Use it for state, not decoration.

## Numbers

### `KpiGrid`
| Prop | Type | |
|---|---|---|
| `items` | `KpiItem[]` | `{ value: ReactNode; label: string; tone?: Tone; href?: string; title?: string }` |
| `columns` | `number?` | Fixed column count; default auto-fits tiles of at least 92 px. |

### `HeroNumber`
`{ value: ReactNode; label?: ReactNode; icon?: string; iconColor?: string; size?: 'md' | 'lg' }` — the big number in
the top-left that answers "what needs my attention" (34 px; `md` is 26 px).

### `Sparkline`
`{ values: number[]; labels?: string[]; ariaLabel: string; color?: string; height?: number;
formatValue?: (value, index) => string }` — SVG line + area, last point marked, one hover title per point. Mirrors
under RTL so time flows in reading direction. `ariaLabel` is required: say what it shows and the values.

### `Donut`
`{ parts: PartItem[]; ariaLabel: string; centerLabel?: string; centerValue?: ReactNode; size?: number;
showLegend?: boolean }` where `PartItem = { label: string; value: number; color: string }`. Center shows the total
unless `centerValue` is given.

### `StackBar`
`{ parts: PartItem[]; ariaLabel: string; showLegend?: boolean }` — one 12 px proportional bar plus legend.

### `BarList`
`{ items: BarItem[]; max?: number; format?: (value: number) => string }` where
`BarItem = { label: string; value: number; display?: string; color?: string; tone?: Tone; href?: string }`.
`max` defaults to the largest value; pass `100` for percentages.

### `Legend`
`{ items: { label: string; value?: ReactNode; color: string }[]; className?: string }`.

## Lists and tables

### `StatusChip`
`{ label: string; color?: string | null; variant?: 'dot' | 'solid'; textColor?: string | null; title?: string }` —
`dot` (default) is a colored dot + heading-colored text; `solid` is the department's button look (`color` background,
`textColor` text) for custom states.

### `ItemList`
`{ items: ListItem[]; className?: string }` where

```ts
interface ListItem {
  key?: string;
  title: ReactNode;        // one line, ellipsized
  meta?: ReactNode;        // muted second line
  right?: ReactNode;       // age, count, small button (<button className="rg-btn-xs">)
  icon?: string;           // FA 4.7 class
  iconColor?: string;      // from the DTO; otherwise the tone's color
  tone?: Tone;             // also picks a default icon when `icon` is not set
  href?: string;           // makes the title a link
  accentColor?: string;    // 3 px leading border (call priority)
  isNew?: boolean;         // brief highlight (a new call)
}
```

### `Agenda`
`{ items: AgendaItem[] }` where `AgendaItem = { key?; day: string; time: string; title: ReactNode; meta?: ReactNode;
right?: ReactNode; href?: string }`. Format `day`/`time` with `ctx.format` (department zone). The day/time column never
wraps ("01:57 PM" stays on one line); it is at least 58 px and grows to fit.

### `DenseTable<TRow>`
| Prop | Type | |
|---|---|---|
| `columns` | `TableColumn<TRow>[]` | `{ key; header; render?(row); muted?; align?: 'start' \| 'end'; maxWidth? }` |
| `rows` | `TRow[]?` | Flat rows… |
| `groups` | `TableGroup<TRow>[]?` | …or collapsible groups `{ key; label; meta?; rows }` (personnel board). |
| `rowKey` | `(row) => string` | |
| `collapsedGroups` / `onToggleGroup` | `string[]?` / `(key) => void` | Persist with `ctx.updateSettings({ collapsed }, { refetch: false })`. |
| `highlightRowKeys` | `string[]?` | Brief highlight for changed rows. |
| `emptyText` / `caption` | `string?` | |

Sticky header; cells do not wrap (ellipsis with `maxWidth`).

### `ProtectedText`
`{ value: DashboardProtectedText | string | null | undefined; placeholder?: string }` — renders the DTO's
`{ Value, IsProtected, CanReveal }` (plan §6.4, §9A): plain text when not protected; otherwise a shield and a muted
placeholder (default "Protected"; pass e.g. `t('W.messages.inbox.ProtectedSubject')`). When `CanReveal` and the page can
step up, it is a button that opens the same step-up as every ADP page; when the viewer's role cannot view the field it
is a plain shield with "Your role can't view this". Never render the server's `REDACTED` string yourself.

## States

### `EmptyState`
`{ icon?: string; text: string; linkText?: string; href?: string; onAction?: () => void }` — icon + one sentence +
optional primary link ("No active calls" / "Create a call").

### `Note`, `SubHeading`
`Note` is the muted footnote under a chart (`{ children; style? }`); `SubHeading` the small uppercase label between
sections.

## Map

### `MiniMap`
| Prop | Type | |
|---|---|---|
| `markers` | `MiniMapMarker[]` | `{ id; lat; lng; color?; radius?; tooltip?; kind?: 'dot' \| 'station'; layer? }` |
| `ariaLabel` | `string` | Required. |
| `center` / `zoom` | `[lat, lng]?` / `number?` | Used when there are no markers. |
| `fit` | `boolean?` | Fit to the markers when the set of ids changes (default true). |
| `hiddenLayers` | `string[]?` | Marker `layer` keys to hide (legend toggles). |
| `legend` | `ReactNode?` | Overlaid bottom-start (`<label><input type="checkbox"/> Calls</label>`). |
| `geoJsonLayers` | `MiniMapGeoJsonLayer[]?` | `{ id: string; data: GeoJSON.GeoJsonObject; color?: string; visible?: boolean }` — drawn under the markers: lines and polygon outlines in `color` (polygons filled at 15 %), points as small circles. `visible: false` hides one. With no markers, the fit uses their bounds. |
| `tileUrl` / `attribution` | `string?` | Overrides the bootstrap tiles (e.g. the department night style); swapped in place, the view is kept. |

Fills the widget body, uses the department's tiles from the bootstrap, and loads Leaflet lazily (only widgets that
render a map pay for it). Zoom, fade and marker animations are off: a hidden tab stalls `requestAnimationFrame`.
Tooltips are plain text. For a chart, use the page's global `window.Chart` (4.4.3) — never bundle a second Chart.js.

## Calling the server (from a widget)

Widgets never call `fetch` themselves. After a write, call `ctx.refresh()` (or wait for the realtime topic).

- `ctx.post<T>(url, body)`: a same-origin **JSON** POST (anti-forgery header; the ADP grant when revealed). Rejects on
  HTTP errors.
- `ctx.submit(url, { query?, form?, json?, ajax?, method? })`: an existing MVC action that binds from the query string
  or **form** and answers with 204, a redirect to HTML or JSON. Resolves `{ ok, status, json? }` and never throws on the
  body (2xx, 3xx and an unfollowed redirect are `ok`; `json` only when the response is JSON; a network failure is
  `{ ok: false, status: 0 }`):

  ```ts
  await ctx.submit('/User/Home/SetCustomAction', { query: { actionType: 2 }, form: { note } });
  const result = await ctx.submit('/User/Dispatch/DispatchCallNow', { form: { callId }, ajax: true });
  if (!result.ok) ctx.toast(t('W.calls.pending.DispatchFailed'));
  ```
- `ctx.api<T>(path, init?, query?)`: the v4 API through the web BFF (`runtime/api.ts` `apiFetchJson`, the helper rg-map
  and Admin Assist use). `path` is relative to `/api/web-bff/`, e.g.
  `ctx.api('api/v4/UnitStatusAlerts/Acknowledge', { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(input) })`. Only BFF-allowed prefixes work (`api/v4/UnitStatusAlerts/`,
  `api/v4/Routes/AcknowledgeDeviation/` for the dashboard). Rejects with `ApiError` (`.status`) on HTTP errors.

## Widget hooks (`../hooks.ts`)

- `useWidgetBadge(ctx, value)` — shows `value` as the header badge while mounted (`null` hides it). A DTO property
  named `Badge` is shown automatically when the widget sets nothing.

## Seeing them

`artifacts/home-dashboard-harness/showcase.html` renders every primitive (the harness README is the page itself:
`src/showcase.tsx`).
