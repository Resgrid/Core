# Resgrid Offline-First Architecture

**Status:** Design / proposal
**Author:** Resgrid IC backend work (RIC-T39 follow-on)
**Last updated:** 2026-06-24
**Applies to:** Resgrid **IC** app (new, `../IC`), Resgrid **Unit** app (existing, `../Unit`), Resgrid **Core** backend (this repo)

> This document is the single source of truth for how the Resgrid mobile apps work
> **fully offline** and **sync back** when reconnected. It is written so the **Unit**
> app team can implement the identical pattern — both apps share one design.

---

## 1. Goal & scenario

A responder logs in and **syncs at the start of a shift** (online, usually on station
WiFi) and pulls down everything they need. They then drive to an incident where there is
**no cell or WiFi**. The app must remain **useful offline** — they can view calls / the
command board / roster / maps, and they can **take actions** (set status, set location,
check in, assign resources, complete objectives, add annotations, etc.). Those actions are
**captured locally** and **synced back** to Resgrid automatically when the device returns
to connectivity (at the incident if cell returns, or back at station/office).

**Hard requirements**

1. Full read functionality offline for the shift's working data set.
2. Full write capture offline, replayed on reconnect, with no lost or duplicated work.
3. **Offline mapping** — map tiles for the operational area must be available with no network.
4. One design, documented, implementable in **both IC and Unit**.

**Non-goals (v1)**

- Real-time multi-device collaborative editing while offline (no CRDTs). Resgrid writes are
  overwhelmingly *additive, timestamped intent-events*, not concurrent edits to a shared document.
- Offline geocoding / turn-by-turn routing (vector search + routing need network; we cache
  tiles and known POIs/destinations only). Documented as a limitation.

---

## 2. Why this design (decision record)

**Decision:** *Evolve* the persistence + outbox stack the Unit app already uses
(**Zustand + MMKV + an offline event queue**) into a complete sync layer. Do **not**
introduce a new on-device database or a managed sync engine for v1.

The Unit app is already ~40% of the way here. State of the world today (`../Unit`):

| Capability | Today | Gap to close |
|---|---|---|
| Persistence | Zustand 4.5.7 + **MMKV 3.1.0** (encrypted), `zustandStorage` adapter | Most domain stores (e.g. `calls`) are **not persisted** → nothing to read offline |
| Write outbox | **Exists**: `src/stores/offline-queue/store.ts` (4 event types, retry/backoff) drained by `src/services/offline-event-manager.service.ts` | Only 4 event types; **no ordering, no idempotency, no dependencies**; relies on backend being idempotent |
| Read cache | `src/api/common/cached-client.ts` + `cache-manager.ts`, 5-min TTL in MMKV | **Does not serve stale data when offline** — returns only *fresh* entries. Biggest single blocker |
| Maps | **@rnmapbox/maps 10.2.10** (Mapbox, native v11.16.2) | `offlineManager.createPack()` supported but unused |
| Realtime | `@microsoft/signalr 8.0.17`, 2 hubs (update + geo) | Needs reconnect reconciliation |
| Auth offline | Tokens in MMKV, `offline_access` scope, network-vs-401 already distinguished in `client.tsx` | Mostly fine |

**Alternatives considered and rejected for v1:**

- **Embedded SQLite (op-sqlite + Drizzle).** More robust at scale, but the per-shift/per-incident
  working set is hundreds–low-thousands of records, not millions; SQL's query/row-write edge is
  marginal here, and retrofitting both apps' domain stores onto a new persistence layer is a large
  migration. **Kept as a documented upgrade path** for individual hot datasets (see §11).
- **Managed sync engine (PowerSync / ElectricSQL / WatermelonDB / RxDB).** Least custom sync code,
  but PowerSync/Electric sync from the *database* via Postgres CDC and route writes around your
  service layer — that fights Resgrid's heavy server-side business logic (billing, workflows,
  accountability) and its dual SQL Server + PostgreSQL backends. WatermelonDB/RxDB still require a
  full persistence-layer rewrite in both apps. Rejected.

**Consequences:** lowest risk, fastest to ship in both apps, one mental model. The cost is that we
own the sync logic (orchestrator, conflict rules, idempotency) — which this document specifies.

---

## 3. Architecture overview

```
┌──────────────────────────── Device (IC app / Unit app) ────────────────────────────┐
│                                                                                      │
│   UI (React / Expo Router)                                                           │
│     │  reads (reactive)              ▲ optimistic apply                              │
│     ▼                                │                                               │
│   Zustand stores  ──── persist ────► MMKV (encrypted)  ◄── stale-while-offline reads │
│     │                                                                                │
│     │ write intent                                                                   │
│     ▼                                                                                │
│   Outbox (offline-queue store, MMKV)  ── ordered, idempotent ──┐                     │
│                                                                │                     │
│   Sync Orchestrator  ◄─────────────────────────────────────────┘                    │
│     • fullSync()  (shift start)                                                      │
│     • drain()     (replay outbox on reconnect)                                       │
│     • pullDelta() (reconcile server-authoritative truth)                             │
│     • map pre-download (offlineManager.createPack)                                   │
│     │                                                                                │
│   NetInfo (online/offline)        SignalR (live updates when online)                │
└─────┼────────────────────────────────────┼─────────────────────────────────────────┘
      │ REST v4                             │ CQRS push (callsUpdated, incidentCommandUpdated…)
      ▼                                     ▼
┌──────────────────────────── Resgrid Core backend (this repo) ───────────────────────┐
│  v4 REST controllers + Services + Repos    CQRS/SignalR (ICoreEventService)          │
│  NEW: delta endpoints, idempotent creates, tombstones, sync bundle (see §9)          │
└──────────────────────────────────────────────────────────────────────────────────┘
```

Three sync phases, described in §6–§8: **(1) shift-start hydration**, **(2) offline operation**,
**(3) reconnect sync-back**.

---

## 4. Local persistence model

**Keep MMKV + Zustand `persist`.** Every store that holds data needed offline must:

1. Wrap its store in `persist(...)` using the existing `zustandStorage` adapter
   (`src/lib/storage/index.tsx` → `createJSONStorage(() => zustandStorage)`).
2. Use `partialize` to persist only durable server data + sync metadata (exclude transient flags
   like `isLoading`).
3. Carry per-store sync metadata: `lastSyncAt: number`, and where applicable a server `syncToken`/
   high-water timestamp for delta pulls.

> **Action item:** the `calls` store (and any other currently-ephemeral domain store) must be
> converted to `persist`. Today `src/stores/calls/store.ts` re-fetches on launch and has nothing to
> show offline. This is the first concrete code change in the Unit app and the default for every IC
> store.

**Stale-while-offline (critical fix).** Upgrade `cached-client.ts` so that when
`NetInfo.isConnected === false` (or a request fails with a network error), it **returns the last
cached value even if its TTL expired**, flagged as stale. Today it only returns *fresh* entries, so
offline reads return nothing. The cache becomes the read fallback; persisted Zustand stores are the
primary offline read source.

Read precedence offline: **persisted store → stale cache → empty/typed-default** (never throw).

**Storage budget.** MMKV holds JSON; keep persisted payloads to the working set. Prune on incident
close (drop closed-incident command boards, their map packs, completed/uploaded outbox entries).

---

## 5. The outbox (generalized write model)

Generalize the existing `offline-queue` from 4 hard-coded event types to a **typed intent-event**
that can represent any mutation in either app.

### 5.1 Event contract

```ts
interface QueuedEvent {
  id: string;                 // client UUID — ALSO the idempotency key sent to the server
  type: QueuedEventType;      // discriminator → handler + endpoint (see registry below)
  entityType: string;         // 'IncidentCommand' | 'UnitStatus' | 'CheckIn' | 'Annotation' | …
  op: 'create' | 'update' | 'delete' | 'action';
  payload: Record<string, unknown>;   // request body; for creates, INCLUDES the client-generated GUID PK
  dependsOn?: string;         // id of a prior queued event that must COMPLETE first (FK ordering)
  clientCreatedAt: number;    // ms — authoritative timestamp for last-write-wins
  // existing fields retained:
  status: 'pending' | 'processing' | 'failed' | 'completed';
  retryCount: number; maxRetries: number;
  createdAt: number; lastAttemptAt?: number; nextRetryAt?: number; error?: string;
}
```

This is a superset of today's shape — existing events migrate by adding `entityType`/`op`/
`clientCreatedAt`/`dependsOn`. Persistence (MMKV key `offline-queue-storage`, `partialize`) is unchanged.

### 5.2 Handler registry

Replace the per-type `switch` in `offline-event-manager.service.ts` with a registry mapping
`QueuedEventType → { apiCall, onSuccess(serverResult), isIdempotent }`. Adding a new offline
mutation = one registry entry, in either app. Today's 4 handlers (UNIT_STATUS, LOCATION_UPDATE,
CALL_IMAGE_UPLOAD, CHECK_IN) become registry entries.

### 5.3 Drainer changes (correctness)

The current drainer (`Promise.allSettled`, 3 concurrent, **no ordering**) is unsafe once events have
dependencies (e.g. *assign resource* offline before *establish command* has synced). Required changes:

- **Order by `createdAt`** and **honor `dependsOn`**: an event whose dependency is not yet
  `completed` is skipped this pass (stays pending). Independent events may still run concurrently.
- **Idempotency on replay** (see §5.4) so a partial success + retry never double-applies.
- Keep: netinfo gate, AppState trigger + 10 s interval, exponential backoff, max-retries → `failed`
  with manual retry. Add a **dead-letter** surfacing UI for permanently failed events (today they
  silently sit in `failed`).

### 5.4 Idempotency (the key to safe replay)

Two mechanisms, chosen per entity:

- **Client-generated GUID PK for creates.** Resgrid entities already use string-GUID PKs that the
  *service* sets via `Guid.NewGuid().ToString()`. Change create endpoints to **honor a client-supplied
  id when present** and **upsert by PK**. The client generates the GUID offline, stores it locally,
  and includes it in the payload → replaying the create is a no-op the second time. This makes
  additive records (annotations, objectives, resource/role assignments, ad-hoc resources) safe.
- **Idempotency key for actions.** Append/telemetry actions (check-in, set status, set location)
  send `id` (the event UUID) as an idempotency key; the server dedups on
  `(DepartmentId, UserId, IdempotencyKey)` within a window, or simply accepts last-write-wins by
  `clientCreatedAt` (a replayed status with the same timestamp is harmless).

### 5.5 Optimistic apply

On a write the app: (1) generates the GUID/event, (2) **applies the change to the Zustand store
immediately** (so the UI updates offline) with a `_pendingSync` flag, (3) enqueues the outbox event.
On successful drain, clear `_pendingSync` and reconcile with the server result. On permanent failure,
mark the local record `_syncError` and surface it (do **not** silently roll back field work).

---

## 6. Phase 1 — Shift-start hydration (online)

`SyncOrchestrator.fullSync()`, run on login / "Sync now":

1. **Bulk pull** all working data in parallel and persist to stores. For IC: active calls + call
   metadata, command boards for active incidents, lanes/resources/objectives/timers/roles/annotations,
   accountability/check-in status, mutual-aid + ad-hoc resources, department units/personnel/statuses,
   groups, POIs/destinations, protocols, contacts, config. For Unit: its existing `init()` set, now
   persisted, plus routes/protocols/weather.
2. **Download offline map packs** (see §10) for the operational area and any active-incident bounds.
3. Record `lastSyncAt` / per-store high-water timestamps; show progress + a "Synced ✓ (time)" state.
4. Pre-warm SignalR so live updates keep the cache hot while still online.

Optional backend optimization: a single `/Sync/Bundle` aggregate endpoint (§9) to cut round-trips.
v1 can fan out existing `GetAll*` calls.

---

## 7. Phase 2 — Offline operation

- **Reads**: served from persisted Zustand stores; `cached-client` returns stale-when-offline.
  NetInfo drives a global "Offline" banner + per-record "pending sync" chips.
- **Writes**: optimistic apply (§5.5) → outbox enqueue. The UI never blocks on the network.
- **Maps**: render from the downloaded offline pack; user location from GPS (works offline);
  annotations/markers are local store data.
- **Auth**: tokens already persist in MMKV; refresh fails gracefully offline (existing
  network-vs-401 logic in `client.tsx`). Ensure the access token's absence/expiry while offline does
  **not** force logout — gate logout on a *non-network* 401 only (already the case) and allow the app
  to operate read/write-to-outbox with an expired token; the outbox drains after a successful refresh
  on reconnect.

---

## 8. Phase 3 — Reconnect sync-back

NetInfo offline→online transition triggers, in order:

1. **Refresh auth** if needed (existing flow).
2. **Drain the outbox** (§5.3) — ordered, dependency-aware, idempotent. Each success updates the
   local record with the server's canonical result and clears `_pendingSync`.
3. **Pull delta / reconcile** (§9): fetch server changes since `lastSyncAt` and merge using the
   conflict policy (§8.1). This catches edits made by *others* (dispatch, other responders) while
   this device was offline.
4. **Reconnect SignalR** and resume live updates; set `lastSyncAt = now`.

### 8.1 Conflict-resolution policy (per entity class)

| Class | Examples | Policy |
|---|---|---|
| **Telemetry / time-series** | unit/personnel status, location, check-in | **Server-authoritative, accept by timestamp (LWW).** Replays harmless. |
| **Additive records** | annotations, timeline/log entries, objectives, resource & role assignments, ad-hoc resources, call notes/images | **Client GUID PK → idempotent create.** No real conflict; duplicates impossible via PK. |
| **Mutable singletons** | the IncidentCommand, lane structure, action plan, call fields | **LWW by `ModifiedOn`**; last writer wins, prior value recorded to the timeline. The one-active-command unique index (M0077) already prevents the worst split-brain. |
| **Deletes** | removed lanes/assignments/resources | **Tombstones** (soft-delete `DeletedOn`) so delta pull propagates the removal to all clients. |

Field-ops reality: contention is rare because each responder mostly writes *their own* status/location/
check-in and *adds* records. LWW + idempotent-additive covers the overwhelming majority; the timeline
preserves an audit trail when a singleton is overwritten.

---

## 9. Backend changes required (Core repo)

These land in this repo as the next backend chunk (migrations start at **M0081**, never renumber —
see the IC backend state doc). All are additive and apply to **both** SQL Server and PostgreSQL.

1. **Consistent change-tracking columns.** Ensure offline-relevant entities have
   `CreatedOn`, `ModifiedOn` (set on every write), and `DeletedOn` (soft-delete/tombstone). Many IC
   entities have `CreatedOn`/`Timestamp` already; audit and backfill the rest.
2. **Idempotent creates.** v4 create endpoints honor a **client-supplied GUID PK** and upsert by PK
   (today the services overwrite with `Guid.NewGuid()`). Guard for cross-department ownership exactly
   as the existing IC create paths do.
3. **Action idempotency keys.** Check-in / status / location endpoints accept an `IdempotencyKey`
   (the outbox event id) and dedup on `(DepartmentId, UserId, IdempotencyKey)` within a window — or
   rely on LWW-by-timestamp where natural.
4. **Delta endpoint(s).** `GET /api/v4/Sync/Changes?since={utcIso}&types=Calls,IncidentCommand,…`
   returning, per type, `created/updated` rows and `deleted` ids (from tombstones) with a new
   server high-water `syncToken`. v1 may implement per-domain `GetChangedSince` and full-refetch the
   small reference sets; build true deltas first for the large sets (messages, call/audit history).
5. **(Optional) `GET /api/v4/Sync/Bundle`** — one aggregate shift-start pull to reduce round-trips.

> CQRS/SignalR already publishes live updates including `IncidentCommandUpdated = 22`
> (`ICoreEventService.IncidentCommandUpdatedAsync`). The delta endpoints are the *catch-up* path for
> what was missed while offline; SignalR is the *live* path while online. Keep both.

---

## 10. Offline mapping (@rnmapbox/maps)

Mapbox is already the map engine (`@rnmapbox/maps 10.2.10`, native v11). Use its **offline pack**
API — no map-library change.

```ts
await offlineManager.createPack({
  name: `incident-${callId}`,
  styleURL: StyleURL.Street,   // the style already in use
  minZoom: 10,
  maxZoom: 16,                 // cap to bound size; see caveats
  bounds: [[swLng, swLat], [neLng, neLat]],
}, onProgress, onError);
```

**What to pre-download at shift start (§6):**

- **Operational area pack** — wide bounds (department/station AO), lower max zoom (~13–14) for
  context.
- **Per-incident pack(s)** — tight bounds around each active call location, higher max zoom (~16) for
  tactical detail. Created on incident establish; deleted on incident close to reclaim space.

**Caveats (from the rnmapbox issue tracker — design around these):**

- The **Mapbox access token must be set before `createPack`** or iOS can crash. Sequence map init
  before any offline download.
- **Tile-count limits / Mapbox ToS** apply (`setTileCountLimit`); do not bypass. Cap `maxZoom` and
  bounds so a pack stays within budget; surface download size to the user.
- Historical Android bugs around zoom > 15 and pack usability — validate on target devices; keep
  `maxZoom ≤ 16` unless verified.
- All `offlineManager` methods are async (SQLite-backed); show progress and handle partial downloads
  / resume.

**Limitations (documented):** offline geocoding/search and routing are **not** available without
network — cache known POIs/destinations/routes as store data instead. Satellite/imagery packs are
large; default to the vector street style offline.

---

## 11. Reuse across IC and Unit (shared module)

Both apps must run the *same* implementation. Neither app is a monorepo today.

**Recommended:** extract the sync layer into a single shared TypeScript module with a stable public
API, consumed by both apps:

- `SyncOrchestrator` (`fullSync`, `drain`, `pullDelta`, map pre-download)
- the generalized outbox store + handler-registry types
- the `zustandStorage`/MMKV persist helpers (already identical in spirit)
- the stale-while-offline `cached-client` wrapper
- the conflict-resolution helpers + entity policy table
- the Mapbox offline-pack helper

Packaging options, in order of preference:
1. **Private package** `@resgrid/offline-sync` (local `file:`/workspace link or private registry) —
   one source of truth, versioned.
2. A shared folder consumed by both via path alias / git submodule.
3. **Copy with a documented contract** (this doc) — acceptable for v1 if packaging is deferred, as
   long as both copies stay in lockstep.

App-specific pieces stay in each app: the **entity registry** (which stores/endpoints exist), the
**data scope** to hydrate, and the **map bounds** logic. The orchestrator, outbox, conflict engine,
and map helper are shared verbatim.

---

## 12. Per-app offline data scope

**IC app** — "useful offline at an incident": the command board for active incidents (lanes,
resources, objectives, timers, roles, annotations, timeline), accountability/PAR status, mutual-aid +
ad-hoc resources, department units/personnel/statuses, call detail + metadata, POIs/destinations,
protocols, maps for the incident area. Offline writes: establish/transfer/close command, lane CRUD,
resource assign/move/release, role assign/remove, objective complete, timer start/ack, annotations,
check-ins, status/location.

**Unit app** — its current `init()` data made durable: active calls + metadata, units, personnel +
statuses + staffing, dispatches, contacts, groups, POIs, routes, protocols, weather. Offline writes:
the existing 4 (status, location, call image, check-in) plus call notes/files/close as needed.

---

## 13. Implementation sequencing

1. **Backend (this repo):** change-tracking columns + idempotent creates + action idempotency keys
   (migrations M0081+), then delta endpoint(s). Each is additive and independently shippable.
2. **Shared sync module:** generalize the outbox + handler registry; build `SyncOrchestrator`;
   upgrade `cached-client` to stale-while-offline; Mapbox offline-pack helper.
3. **Unit app adoption (lower risk, proves the pattern):** persist the ephemeral stores; wire the
   orchestrator; pre-download maps; move the 4 existing events onto the generalized outbox.
4. **IC app (Part B):** built offline-first from the start on the shared module.
5. **Reconnect reconciliation + SignalR catch-up:** delta pull on reconnect; conflict policy; dead-letter UI.

---

## 14. Risks & open questions

- **Backfilling `ModifiedOn`/tombstones** across legacy Resgrid entities is the largest backend
  effort; scope delta sync to offline-relevant entities first.
- **Idempotent creates** require auditing every targeted v4 create endpoint to honor a client PK
  safely (ownership guards must hold).
- **Mapbox tile budget / ToS** for fleet-wide offline downloads — confirm licensing/limits with
  Mapbox for the expected device count and AO sizes.
- **MMKV size ceiling** for very large datasets (long message/call history) — the documented upgrade
  path is op-sqlite for *that dataset only*, behind the same store interface.
- **Clock skew** affects LWW — prefer server `ModifiedOn` for singletons; use `clientCreatedAt` only
  for the device's own telemetry.
- **Long-offline token expiry** — verify the refresh-token lifetime (`offline_access`) exceeds a
  realistic offline shift; if not, allow a longer-lived refresh for the mobile scope.

---

## 15. References

- Unit app stack: Zustand+MMKV stores (`src/stores/*`), outbox (`src/stores/offline-queue/store.ts`,
  `src/services/offline-event-manager.service.ts`), cache (`src/api/common/cached-client.ts`,
  `src/lib/cache/cache-manager.ts`), client (`src/api/common/client.tsx`), maps
  (`src/components/maps/*`), SignalR (`src/services/signalr.service.ts`).
- IC backend (this repo): `Core/Resgrid.Model/IncidentCommand/*`, `Core/Resgrid.Services/IncidentCommandService.cs`,
  v4 controllers under `Web/Resgrid.Web.Services/Controllers/v4/`, CQRS `CqrsEventTypes.IncidentCommandUpdated = 22`.
- Mapbox offline: rnmapbox `offlineManager` docs — https://rnmapbox.github.io/docs/components/offlineManager
- Local-first / sync background: PowerSync RN local-DB options
  (https://powersync.com/blog/react-native-local-database-options), Expo local-first guide
  (https://docs.expo.dev/guides/local-first/), RxDB RN (https://rxdb.info/react-native-database.html).
- Sync patterns: outbox + idempotency (https://microservices.io/patterns/data/transactional-outbox.html),
  offline conflict resolution (https://www.sachith.co.uk/offline-sync-conflict-resolution-patterns-architecture-trade%E2%80%91offs-practical-guide-feb-19-2026/).
