# Bekçi — Guard Tour System · Phase 1 Design

**Date:** 2026-07-10
**Status:** Approved for planning
**Scope:** Phase 1 — the vertical patrol loop

---

## 1. Overview

Bekçi is a multi-tenant SaaS for security guard tour management. Supervisors
define routes made of checkpoints; guards walk those routes and prove they
physically reached each checkpoint by scanning a QR code while their location is
captured. Both roles use a single Flutter app with role-based UI, backed by a
.NET API.

This document specifies **Phase 1 only**: the thinnest end-to-end patrol loop
that produces a working product and de-risks the hardest part — offline sync.

### Full-system context (for orientation, not Phase 1 scope)

```
Organization (tenant)
 └─ Site (e.g. "Mall A")
     ├─ Route (enforceOrder: true/false)
     │   └─ Checkpoint (QR id, GPS coords, geofence radius, sequence #)
     └─ Shift (guard + time window + attached route(s))   ← Phase 2+
         └─ Patrol (a guard's live execution of a route)
             └─ Scan (checkpoint, timestamp, GPS, order-valid?, geo-valid?)
```

The full product also includes shift scheduling, a SignalR live feed, panic/SOS,
and checkpoint notes/photos. These are **out of scope for Phase 1** and each will
get its own spec → plan → build cycle.

---

## 2. Phase 1 Scope

### In scope
- Multi-tenant auth: `Organization`, `User` with `Supervisor` / `Guard` roles.
- Supervisor configuration:
  - Create a `Site`.
  - Create a `Route` with an `EnforceOrder` toggle.
  - Add `Checkpoint`s (QR identifier, GPS lat/lng, geofence radius, sequence #).
- Guard patrol:
  - Log in, see routes at their site, **start a patrol**.
  - Scan checkpoints (QR + GPS); when online, each scan is **sent to the server
    immediately**.
  - When offline, scans are **queued locally and flushed automatically** when
    connection returns (guard is never blocked).
- Supervisor history:
  - View completed/in-progress patrols and each scan (refresh-based, no live push).

### Explicitly deferred to Phase 2+
- Shift scheduling and time windows.
- SignalR live feed (Phase 1 is refresh/pull-to-refresh only).
- Panic / SOS button.
- Checkpoint notes and photos.

### Deliberate Phase 1 stand-in
Because shifts are deferred, a guard is simply **linked to one site** and
**self-selects** a route to start. This placeholder is replaced by shift-based
assignment in Phase 2. No wasted work: the "start a patrol" action is identical
either way.

---

## 3. Tech Stack

Matches existing repos (`sparkion-loyalty`, `ThePlaceApi`).

### Backend
- .NET, Clean Architecture: **Domain / Application / Infrastructure / API**.
- EF Core + **PostgreSQL**.
- JWT auth.
- Tenant isolation via a `TenantId` on every entity plus an EF Core global query
  filter.
- Dockerized (matching existing repos' `Dockerfile` / `docker-compose.yml`).

### Mobile
- Flutter, clean architecture: **presentation / domain / data**.
- **Riverpod** for state management.
- **Dio + Retrofit** for API access.
- **Drift (SQLite)** for a lightweight local store: a read-only cache of
  route/checkpoint reference data + an offline send queue.
- `mobile_scanner` for QR scanning.
- `geolocator` for GPS.
- `connectivity_plus` to trigger sync flushes.

---

## 4. Backend Data Model & API

### 4.1 Domain entities

All entities carry `TenantId` and are filtered globally by it.

| Entity | Key fields | Notes |
|---|---|---|
| `Organization` | Id, Name | The tenant root |
| `User` | Id, Email, PasswordHash, Role (`Supervisor`/`Guard`), SiteId? | Guards linked to one site in Phase 1 |
| `Site` | Id, Name, Address | Belongs to org |
| `Route` | Id, SiteId, Name, EnforceOrder (bool) | |
| `Checkpoint` | Id, RouteId, Name, QrCode (unique-per-route string), Lat, Lng, GeofenceRadiusM, Sequence | `QrCode` is what's printed on the sticker |
| `Patrol` | Id, RouteId, GuardId, StartedAt, CompletedAt?, Status (`InProgress`/`Completed`/`Abandoned`) | One guard's execution of a route |
| `Scan` | Id, PatrolId, CheckpointId, ScannedAt (device time), ReceivedAt (server time), Lat, Lng, GeoValid (bool), OrderValid (bool), IsDuplicate (bool) | Created on device; sent immediately when online, queued when offline |

`Patrol.Id` and `Scan.Id` are **client-generated GUIDs** so the device can create
them offline and uploads stay idempotent.

### 4.2 Multi-tenancy

- JWT carries `TenantId`, `UserId`, and `Role`.
- A `TenantContext` is resolved from the JWT per request.
- EF Core global query filter on `TenantId` ensures no query can leak across
  organizations.

### 4.3 Scan ingestion (the critical endpoint)

`POST /patrols/{id}/scans` accepts **one or more** scans with client-generated
GUIDs. Online, the app posts a single scan the moment it happens; when flushing a
backlog after a dead zone, it posts the queued scans together.

- **Idempotent:** posting a scan with an existing GUID is a no-op. This covers
  both offline-flush retries and the online case where an immediate send times out
  and is retried.
- The device records **when** a scan happened (`ScannedAt` = device time). The
  server records `ReceivedAt` separately.
- The server **re-validates** `GeoValid` and `OrderValid` on ingestion; the
  server verdict is authoritative for reports. (Device values can't be trusted
  blindly.)

### 4.4 API surface (Phase 1)

- **Auth:** `POST /auth/login`
- **Supervisor:**
  - CRUD `/sites`
  - CRUD `/routes`
  - CRUD `/routes/{id}/checkpoints`
  - `GET /patrols` (filter by site/route/guard/date)
  - `GET /patrols/{id}`
- **Guard:**
  - `GET /routes?siteId=` (routes at their site)
  - `POST /patrols` (start; body includes client GUID)
  - `POST /patrols/{id}/scans` (batch sync)
  - `POST /patrols/{id}/complete`

---

## 5. Mobile App: Online-First with Offline Send Queue

### 5.1 Layering

- **presentation** — screens + Riverpod controllers, role-routed at login
  (`Supervisor` → dashboard, `Guard` → patrol home).
- **domain** — entities + repository interfaces + use cases (StartPatrol,
  RecordScan, CompletePatrol).
- **data** — behind each repository: **remote (Retrofit/Dio)** is the primary
  path, plus a **local (Drift/SQLite)** store with two narrow jobs:
  1. a **read-only cache** of route/checkpoint reference data, so a scanned QR can
     be matched and validated even without a connection;
  2. a **send queue** of operations that couldn't be sent immediately.

**The server is the source of truth.** When online, the guard flow talks to the
server directly and records nothing durable on-device beyond the reference cache.
The send queue exists only to bridge dead zones.

### 5.2 Guard patrol flow

1. While online, routes for the guard's site are fetched and **cached to Drift**
   (checkpoints, QR codes, coordinates) as read-only reference data.
2. Guard taps a route → **StartPatrol**. If online, the patrol is created on the
   server immediately (client GUID, status `InProgress`); if offline, it's created
   with a client GUID and enqueued.
3. At each checkpoint:
   - Scan QR (`mobile_scanner`) → match `QrCode` against cached checkpoints.
   - Grab GPS (`geolocator`).
   - Compute `GeoValid` (within radius?) and `OrderValid` (respects
     `EnforceOrder`?).
   - **If online, POST the scan immediately.** If the send fails or the device is
     offline, write it to the send queue (`syncStatus = pending`).
   - Instant on-screen feedback either way — the network round-trip never blocks
     the guard.
4. Guard taps Complete → sent immediately if online, otherwise enqueued.

### 5.3 Send queue

- **Primary path is immediate send.** The queue only holds operations (patrol
  start, scans, completion) that couldn't reach the server.
- Flush triggers: `connectivity_plus` regains connection, a manual "Sync now"
  button, and on-app-resume. Queued rows flip `pending → synced`; failures stay
  `pending` and back off.
- Idempotent sends via client GUIDs → safe to retry, whether the retry is a
  dead-zone flush or an online send that timed out.
- **No merge conflicts by design:** the device generates the operations and the
  server never mutates patrols/scans — only at-least-once delivery deduped by
  GUID.

### 5.4 Supervisor flow (Phase 1)

- Login → dashboard listing patrols (filter by site/route/guard/date).
- Tap a patrol → checkpoint-by-checkpoint results with geo/order flags.
- Pull-to-refresh (live feed arrives in Phase 2).

---

## 6. Checkpoint Verification Logic

- **Proof mechanism:** QR scan **+** GPS geofence.
- **Geofence is a soft signal**, never a blocker. A guard indoors/underground with
  weak GPS can always complete a checkpoint; the mismatch is flagged, not
  rejected.
- `GeoValid` = device location within `GeofenceRadiusM` of the checkpoint's
  coordinates at scan time.
- `OrderValid` = when `EnforceOrder = true`, the scanned checkpoint is the next
  expected in sequence; otherwise always true.
- Both are computed **on the device** at scan time (so it works offline) and
  **re-validated on the server** at ingestion. Server verdict wins for reports.

---

## 7. Error Handling & Edge Cases

### Guard-side
- **Unknown QR** (wrong sticker / not on this route) → clear "This checkpoint
  isn't on your route" message; nothing recorded.
- **Duplicate scan** of the same checkpoint → accept but mark `IsDuplicate`; keep
  the first as authoritative; never silently drop.
- **GPS unavailable/denied** → still record the scan, set `GeoValid = false` with
  reason `location_unavailable`. Geofence is soft — never blocks the guard.
- **Out-of-order scan** with `EnforceOrder = true` → record it, set
  `OrderValid = false`, warn the guard, let them continue. Supervisor sees the
  flag.
- **App killed mid-patrol** → already-sent scans live on the server; any un-sent
  operations remain in the local send queue. On relaunch the guard resumes and the
  queue flushes.
- **Route edited by supervisor after caching** → the active patrol uses its cached
  snapshot; refreshes on next patrol start (no mid-patrol surprises).

### Server-side
- Batch scan ingestion is idempotent (dedupe on client GUID), re-validates
  geo/order, records `ReceivedAt` vs device `ScannedAt`.
- Standard problem-details error responses.
- Tenant query filter guarantees cross-org isolation.

---

## 8. Testing Strategy

### Backend (xUnit)
- Unit tests for order/geo validation and tenant isolation.
- Integration tests (Testcontainers Postgres) for the sync ingestion endpoint,
  including idempotency and re-upload.

### Mobile
- Unit tests for the send queue (immediate send, enqueue-on-failure, flush,
  retry/backoff, GUID dedupe) and scan validation logic.
- Repository tests against an in-memory Drift DB.
- Widget test for the core scan-feedback screen.

### Critical path to prove end-to-end
Scan a checkpoint online → confirm it reaches the server immediately → enter a
dead zone → scan several more checkpoints (one out-of-order, one no-GPS), which
queue locally → regain connectivity → the queue flushes exactly once → server
verdicts match device verdicts → supervisor sees correct flags.

---

## 9. Out of Scope (Phase 2+ backlog)

- Shift scheduling and time windows.
- SignalR live feed for supervisors.
- Panic / SOS button with last-known location.
- Checkpoint notes and photos (media storage).
