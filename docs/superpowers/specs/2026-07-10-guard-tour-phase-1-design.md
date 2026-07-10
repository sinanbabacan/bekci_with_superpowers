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
  - Scan checkpoints (QR + GPS) **fully offline**.
  - Sync queued data when back online.
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
- **Drift (SQLite)** for the offline store + a sync engine.
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
| `Scan` | Id, PatrolId, CheckpointId, ScannedAt (device time), ReceivedAt (server time), Lat, Lng, GeoValid (bool), OrderValid (bool), IsDuplicate (bool) | Created on device, uploaded in batch |

`Patrol.Id` and `Scan.Id` are **client-generated GUIDs** so the device can create
them offline and uploads stay idempotent.

### 4.2 Multi-tenancy

- JWT carries `TenantId`, `UserId`, and `Role`.
- A `TenantContext` is resolved from the JWT per request.
- EF Core global query filter on `TenantId` ensures no query can leak across
  organizations.

### 4.3 Sync ingestion (the critical endpoint)

`POST /patrols/{id}/scans` accepts a **batch** of scans with client-generated
GUIDs.

- **Idempotent:** re-uploading a scan with an existing GUID is a no-op.
- The device is the source of truth for **when** a scan happened
  (`ScannedAt` = device time). The server records `ReceivedAt` separately.
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

## 5. Mobile App: Offline-First Architecture

### 5.1 Layering

- **presentation** — screens + Riverpod controllers, role-routed at login
  (`Supervisor` → dashboard, `Guard` → patrol home).
- **domain** — entities + repository interfaces + use cases (StartPatrol,
  RecordScan, SyncPatrol).
- **data** — two sources behind each repository: **local (Drift/SQLite)** and
  **remote (Retrofit/Dio)**.

**The local Drift DB is the source of truth on-device.** The guard flow never
touches the network directly — it reads/writes Drift; a background sync engine
reconciles with the server.

### 5.2 Guard patrol flow

1. At a site with signal, guard opens app → routes for their site are fetched and
   **cached to Drift** (checkpoints, QR codes, coordinates all stored locally).
2. Guard taps a route → **StartPatrol** creates a local `Patrol` row (client GUID,
   status `InProgress`). Fully offline from here on.
3. At each checkpoint:
   - Scan QR (`mobile_scanner`) → match `QrCode` against cached checkpoints.
   - Grab GPS (`geolocator`).
   - Compute `GeoValid` (within radius?) and `OrderValid` (respects
     `EnforceOrder`?).
   - Write a local `Scan` row with `syncStatus = pending`.
   - Instant feedback, no network.
4. Guard taps Complete → local patrol marked `Completed`, queued for sync.

### 5.3 Sync engine

- A repository-level queue: any `pending` row (patrol start, scans, completion) is
  pushed when connectivity returns.
- Flush triggers: `connectivity_plus` regains connection, a manual "Sync now"
  button, and on-app-resume.
- Idempotent uploads via client GUIDs → safe to retry. Rows flip
  `pending → synced`; failures stay `pending` and back off.
- **Conflict handling is minimal by design:** the device owns patrol/scan
  creation and the server never mutates them, so there is no merge conflict —
  only at-least-once delivery deduped by GUID.

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
- **App killed mid-patrol** → in-progress patrol and scans already persisted in
  Drift; on relaunch the guard resumes where they left off.
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
- Unit tests for the sync engine (pending→synced, retry/backoff, GUID dedupe) and
  scan validation logic.
- Repository tests against an in-memory Drift DB.
- Widget test for the core scan-feedback screen.

### Critical path to prove end-to-end
Start patrol offline → scan several checkpoints (one out-of-order, one no-GPS)
offline → regain connectivity → everything syncs exactly once → server verdicts
match device verdicts → supervisor sees correct flags.

---

## 9. Out of Scope (Phase 2+ backlog)

- Shift scheduling and time windows.
- SignalR live feed for supervisors.
- Panic / SOS button with last-known location.
- Checkpoint notes and photos (media storage).
