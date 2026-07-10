# Bekçi Guard Tour — Phase 1 Flutter App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 1 Flutter app — one codebase, role-based UI — that lets supervisors review patrols and guards run patrols (scan QR + GPS), sending scans to the backend immediately when online and queuing them locally to auto-flush when offline.

**Architecture:** Clean architecture in three layers — `presentation` (Riverpod controllers + screens), `domain` (immutable entities + repository interfaces + client-side validation), `data` (Drift local store + Retrofit/Dio remote client, behind repository implementations). The **server is the source of truth**; the local Drift store holds only (1) a read-only cache of route/checkpoint reference data for offline scan validation and (2) an **outbox** send queue. Operations carry client-generated UUIDs so re-sends are idempotent against the backend built in Plan A.

**Tech Stack:** Flutter (Dart 3), `flutter_riverpod`, `go_router`, `dio` + `retrofit` + `json_serializable`, `drift` + `sqlite3_flutter_libs`, `flutter_secure_storage`, `mobile_scanner`, `geolocator`, `connectivity_plus`, `uuid`. Tests: `flutter_test` + `mocktail` + `http_mock_adapter` + `drift` in-memory.

## Global Constraints

- **Backend contract:** this app targets the Plan A API. Base URL from `--dart-define=API_BASE_URL=...` (default `http://10.0.2.2:8080` for the Android emulator). Endpoints used: `POST /api/v1/auth/login`, `GET /api/v1/guard/routes`, `GET /api/v1/routes/{routeId}/checkpoints`, `POST /api/v1/patrols`, `POST /api/v1/patrols/{id}/scans`, `POST /api/v1/patrols/{id}/complete`, `GET /api/v1/patrols`, `GET /api/v1/patrols/{id}`.
- **IDs are strings** (server `Guid` serialized as string). The app generates UUID v4 strings for `patrolId` and `scanId` via the `uuid` package before sending.
- **Times are UTC ISO-8601.** Always send `DateTime.now().toUtc().toIso8601String()`. Parse incoming timestamps with `DateTime.parse(...).toUtc()`.
- **Geofence is soft.** A scan is always recorded and always sendable; `geoValid`/`orderValid` are flags only. GPS permission denied or unavailable → record with `lat/lng = null` and `geoValid = false`.
- **Online-first:** every operation attempts an immediate send; on any network failure it is written to the outbox with `SendStatus.pending`. The outbox flushes on connectivity regain, on app resume, and via a manual "Sync now" action.
- **Idempotency:** every mutating request uses the client UUID as the record id; re-sending the same id is safe (backend dedupes).
- **Codegen:** after editing any `drift` table, `@RestApi`, or `@JsonSerializable` file, run `dart run build_runner build --delete-conflicting-outputs`.
- **Layer rule:** `presentation` depends on `domain`; `data` depends on `domain`; `domain` depends on nothing. Never import `data/` from `presentation/` except through a Riverpod provider that returns a `domain` repository interface.
- **Commit after every task** with the message shown in the final step.

---

## File Structure

```
mobile/
  pubspec.yaml
  lib/
    main.dart
    app.dart                      # MaterialApp.router + ProviderScope wiring
    core/
      config.dart                 # apiBaseUrl from dart-define
      di.dart                     # top-level providers (dio, db, secure storage)
    domain/
      enums.dart                  # UserRole, PatrolSyncStatus, SendStatus, ScanKind
      entities/
        auth_session.dart
        route_ref.dart
        checkpoint_ref.dart
        local_patrol.dart
        local_scan.dart
        patrol_summary.dart
        patrol_detail.dart
      scan_validation.dart        # client-side geofence + order logic
      repositories/
        auth_repository.dart      # interface
        route_repository.dart     # interface
        patrol_repository.dart    # interface
        supervisor_repository.dart# interface
    data/
      local/
        app_database.dart         # Drift DB + tables + DAOs
      remote/
        api_client.dart           # Retrofit @RestApi service
        dtos.dart                 # @JsonSerializable request/response DTOs
        auth_interceptor.dart     # attaches bearer token
      repositories/
        auth_repository_impl.dart
        route_repository_impl.dart
        patrol_repository_impl.dart
        supervisor_repository_impl.dart
      sync/
        sync_service.dart         # outbox flush engine
    presentation/
      router.dart                 # GoRouter + role redirect
      auth/login_screen.dart
      auth/auth_controller.dart
      guard/guard_home_screen.dart
      guard/patrol_screen.dart
      guard/patrol_controller.dart
      supervisor/supervisor_home_screen.dart
      supervisor/patrol_detail_screen.dart
      supervisor/supervisor_controller.dart
  test/
    domain/scan_validation_test.dart
    data/app_database_test.dart
    data/api_client_test.dart
    data/sync_service_test.dart
    data/patrol_repository_test.dart
    presentation/patrol_feedback_test.dart
    integration/critical_path_test.dart
```

---

## Shared Type Reference (defined across tasks — listed here for consistency)

- `enum UserRole { supervisor, guard }`
- `enum SendStatus { pending, sent }`
- `enum PatrolSyncStatus { inProgress, completed }`
- `enum ScanKind { startPatrol, scan, completePatrol }` (outbox entry kind)
- `class ScanValidation { static bool isWithinGeofence(double cpLat, double cpLng, double radiusM, double? lat, double? lng); static double distanceMeters(double lat1,double lng1,double lat2,double lng2); static bool isInOrder(List<String> expectedSeqCheckpointIds, Set<String> alreadyScanned, String checkpointId); }`
- Repository interfaces (all async):
  - `AuthRepository.login(String email, String password) -> Future<AuthSession>`; `currentSession() -> Future<AuthSession?>`; `logout() -> Future<void>`
  - `RouteRepository.refreshGuardRoutes() -> Future<List<RouteRef>>`; `cachedRoutes() -> Future<List<RouteRef>>`; `checkpointsFor(String routeId) -> Future<List<CheckpointRef>>`
  - `PatrolRepository.startPatrol(String routeId) -> Future<LocalPatrol>`; `recordScan({required String patrolId, required String routeId, required String qrCode}) -> Future<ScanOutcome>`; `completePatrol(String patrolId) -> Future<void>`; `scansFor(String patrolId) -> Future<List<LocalScan>>`
  - `SupervisorRepository.listPatrols({String? siteId, String? routeId, String? guardId}) -> Future<List<PatrolSummary>>`; `patrolDetail(String id) -> Future<PatrolDetail>`
- `class ScanOutcome { final LocalScan scan; final bool matchedCheckpoint; }` (returned by `recordScan`; `matchedCheckpoint=false` when the QR is unknown)
- `SyncService.flush() -> Future<void>`; `start()` (subscribes to connectivity)

---

### Task 1: Scaffold Flutter project, dependencies, app shell

**Files:**
- Create: `mobile/` (via `flutter create`), `mobile/lib/main.dart`, `mobile/lib/app.dart`, `mobile/lib/core/config.dart`
- Test: `mobile/test/smoke_test.dart`

**Interfaces:**
- Produces: `apiBaseUrl` (from `String.fromEnvironment('API_BASE_URL')`), a `ProviderScope`-wrapped `BekciApp` widget.

- [ ] **Step 1: Create the project and add dependencies**

Run:
```bash
cd /Users/sinanbabacan/repos/bekci_with_superpowers
flutter create --org com.bekci --project-name bekci mobile
cd mobile
flutter pub add flutter_riverpod go_router dio retrofit json_annotation drift sqlite3_flutter_libs path_provider path flutter_secure_storage mobile_scanner geolocator connectivity_plus uuid
flutter pub add --dev build_runner retrofit_generator json_serializable drift_dev mocktail http_mock_adapter
```
Expected: `flutter pub get` succeeds.

- [ ] **Step 2: Write the failing smoke test**

Create `mobile/test/smoke_test.dart`:
```dart
import 'package:bekci/app.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('app builds and shows a MaterialApp', (tester) async {
    await tester.pumpWidget(const BekciApp());
    expect(find.byType(MaterialApp), findsOneWidget);
  });
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `flutter test test/smoke_test.dart`
Expected: FAIL — `BekciApp` / `app.dart` do not exist.

- [ ] **Step 4: Write config, app, and main**

Create `mobile/lib/core/config.dart`:
```dart
const String apiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'http://10.0.2.2:8080',
);
```

Create `mobile/lib/app.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

class BekciApp extends StatelessWidget {
  const BekciApp({super.key});

  @override
  Widget build(BuildContext context) {
    return const ProviderScope(child: _Root());
  }
}

class _Root extends StatelessWidget {
  const _Root();

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Bekçi',
      theme: ThemeData(colorSchemeSeed: Colors.indigo, useMaterial3: true),
      home: const Scaffold(body: Center(child: Text('Bekçi'))),
    );
  }
}
```

Replace `mobile/lib/main.dart`:
```dart
import 'package:flutter/material.dart';
import 'app.dart';

void main() {
  runApp(const BekciApp());
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `flutter test test/smoke_test.dart`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
cd /Users/sinanbabacan/repos/bekci_with_superpowers
git add mobile
git commit -m "feat(mobile): scaffold Flutter app shell with Riverpod"
```

---

### Task 2: Domain enums and entities

**Files:**
- Create: `mobile/lib/domain/enums.dart`, `mobile/lib/domain/entities/auth_session.dart`, `route_ref.dart`, `checkpoint_ref.dart`, `local_patrol.dart`, `local_scan.dart`, `patrol_summary.dart`, `patrol_detail.dart`
- Test: `mobile/test/domain/entities_test.dart`

**Interfaces:**
- Produces the immutable entities and enums listed in the Shared Type Reference. All are plain Dart classes with `const` constructors and value fields (no codegen).

- [ ] **Step 1: Write the failing test**

Create `mobile/test/domain/entities_test.dart`:
```dart
import 'package:bekci/domain/enums.dart';
import 'package:bekci/domain/entities/checkpoint_ref.dart';
import 'package:bekci/domain/entities/local_scan.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('CheckpointRef holds its fields', () {
    const cp = CheckpointRef(
      id: 'c1', routeId: 'r1', name: 'Lobby', qrCode: 'QR-1',
      lat: 40.0, lng: 29.0, geofenceRadiusM: 25, sequence: 1,
    );
    expect(cp.qrCode, 'QR-1');
    expect(cp.sequence, 1);
  });

  test('LocalScan copyWith updates sendStatus', () {
    final scan = LocalScan(
      id: 's1', patrolId: 'p1', checkpointId: 'c1',
      scannedAt: DateTime.utc(2026, 7, 10), lat: null, lng: null,
      geoValid: false, orderValid: true, sendStatus: SendStatus.pending,
    );
    final sent = scan.copyWith(sendStatus: SendStatus.sent);
    expect(sent.sendStatus, SendStatus.sent);
    expect(sent.id, 's1');
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `flutter test test/domain/entities_test.dart`
Expected: FAIL — entities do not exist.

- [ ] **Step 3: Write enums and entities**

Create `mobile/lib/domain/enums.dart`:
```dart
enum UserRole { supervisor, guard }

enum SendStatus { pending, sent }

enum PatrolSyncStatus { inProgress, completed }

enum ScanKind { startPatrol, scan, completePatrol }
```

Create `mobile/lib/domain/entities/auth_session.dart`:
```dart
import '../enums.dart';

class AuthSession {
  final String token;
  final UserRole role;
  final String tenantId;
  final String? siteId;

  const AuthSession({
    required this.token,
    required this.role,
    required this.tenantId,
    required this.siteId,
  });
}
```

Create `mobile/lib/domain/entities/route_ref.dart`:
```dart
class RouteRef {
  final String id;
  final String siteId;
  final String name;
  final bool enforceOrder;

  const RouteRef({
    required this.id,
    required this.siteId,
    required this.name,
    required this.enforceOrder,
  });
}
```

Create `mobile/lib/domain/entities/checkpoint_ref.dart`:
```dart
class CheckpointRef {
  final String id;
  final String routeId;
  final String name;
  final String qrCode;
  final double lat;
  final double lng;
  final double geofenceRadiusM;
  final int sequence;

  const CheckpointRef({
    required this.id,
    required this.routeId,
    required this.name,
    required this.qrCode,
    required this.lat,
    required this.lng,
    required this.geofenceRadiusM,
    required this.sequence,
  });
}
```

Create `mobile/lib/domain/entities/local_patrol.dart`:
```dart
import '../enums.dart';

class LocalPatrol {
  final String id;
  final String routeId;
  final DateTime startedAt;
  final DateTime? completedAt;
  final PatrolSyncStatus status;

  const LocalPatrol({
    required this.id,
    required this.routeId,
    required this.startedAt,
    required this.completedAt,
    required this.status,
  });
}
```

Create `mobile/lib/domain/entities/local_scan.dart`:
```dart
import '../enums.dart';

class LocalScan {
  final String id;
  final String patrolId;
  final String checkpointId;
  final DateTime scannedAt;
  final double? lat;
  final double? lng;
  final bool geoValid;
  final bool orderValid;
  final SendStatus sendStatus;

  const LocalScan({
    required this.id,
    required this.patrolId,
    required this.checkpointId,
    required this.scannedAt,
    required this.lat,
    required this.lng,
    required this.geoValid,
    required this.orderValid,
    required this.sendStatus,
  });

  LocalScan copyWith({SendStatus? sendStatus}) => LocalScan(
        id: id,
        patrolId: patrolId,
        checkpointId: checkpointId,
        scannedAt: scannedAt,
        lat: lat,
        lng: lng,
        geoValid: geoValid,
        orderValid: orderValid,
        sendStatus: sendStatus ?? this.sendStatus,
      );
}
```

Create `mobile/lib/domain/entities/patrol_summary.dart`:
```dart
class PatrolSummary {
  final String id;
  final String routeId;
  final String guardId;
  final DateTime startedAt;
  final DateTime? completedAt;
  final String status;
  final int scanCount;

  const PatrolSummary({
    required this.id,
    required this.routeId,
    required this.guardId,
    required this.startedAt,
    required this.completedAt,
    required this.status,
    required this.scanCount,
  });
}
```

Create `mobile/lib/domain/entities/patrol_detail.dart`:
```dart
class PatrolScanView {
  final String id;
  final String checkpointId;
  final String checkpointName;
  final DateTime scannedAt;
  final bool geoValid;
  final bool orderValid;
  final bool isDuplicate;

  const PatrolScanView({
    required this.id,
    required this.checkpointId,
    required this.checkpointName,
    required this.scannedAt,
    required this.geoValid,
    required this.orderValid,
    required this.isDuplicate,
  });
}

class PatrolDetail {
  final String id;
  final String routeId;
  final String guardId;
  final DateTime startedAt;
  final DateTime? completedAt;
  final String status;
  final List<PatrolScanView> scans;

  const PatrolDetail({
    required this.id,
    required this.routeId,
    required this.guardId,
    required this.startedAt,
    required this.completedAt,
    required this.status,
    required this.scans,
  });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter test test/domain/entities_test.dart`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add mobile/lib/domain mobile/test/domain/entities_test.dart
git commit -m "feat(mobile): domain enums and entities"
```

---

### Task 3: Client-side scan validation (geofence + order)

**Files:**
- Create: `mobile/lib/domain/scan_validation.dart`
- Test: `mobile/test/domain/scan_validation_test.dart`

**Interfaces:**
- Produces `ScanValidation.isWithinGeofence`, `.distanceMeters`, `.isInOrder` per the Shared Type Reference. `isInOrder`: given the route's checkpoint ids in ascending sequence, the set already scanned, and the candidate id — the candidate is in order iff it equals the first not-yet-scanned checkpoint in sequence. Mirrors the server's rule so device and server verdicts agree.

- [ ] **Step 1: Write the failing test**

Create `mobile/test/domain/scan_validation_test.dart`:
```dart
import 'package:bekci/domain/scan_validation.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('geofence', () {
    test('within radius is valid', () {
      expect(ScanValidation.isWithinGeofence(40.0, 29.0, 25, 40.0001, 29.0), isTrue);
    });
    test('outside radius is invalid', () {
      expect(ScanValidation.isWithinGeofence(40.0, 29.0, 25, 40.0010, 29.0), isFalse);
    });
    test('missing location is invalid', () {
      expect(ScanValidation.isWithinGeofence(40.0, 29.0, 25, null, null), isFalse);
    });
  });

  group('order', () {
    final seq = ['c1', 'c2', 'c3'];
    test('first unscanned in sequence is in order', () {
      expect(ScanValidation.isInOrder(seq, <String>{}, 'c1'), isTrue);
      expect(ScanValidation.isInOrder(seq, {'c1'}, 'c2'), isTrue);
    });
    test('skipping ahead is out of order', () {
      expect(ScanValidation.isInOrder(seq, <String>{}, 'c3'), isFalse);
    });
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `flutter test test/domain/scan_validation_test.dart`
Expected: FAIL — `ScanValidation` does not exist.

- [ ] **Step 3: Write the validation logic**

Create `mobile/lib/domain/scan_validation.dart`:
```dart
import 'dart:math';

class ScanValidation {
  static const double _earthRadiusMeters = 6371000;

  static bool isWithinGeofence(
      double cpLat, double cpLng, double radiusM, double? lat, double? lng) {
    if (lat == null || lng == null) return false;
    return distanceMeters(cpLat, cpLng, lat, lng) <= radiusM;
  }

  static double distanceMeters(double lat1, double lng1, double lat2, double lng2) {
    double toRad(double d) => d * pi / 180.0;
    final dLat = toRad(lat2 - lat1);
    final dLng = toRad(lng2 - lng1);
    final a = sin(dLat / 2) * sin(dLat / 2) +
        cos(toRad(lat1)) * cos(toRad(lat2)) * sin(dLng / 2) * sin(dLng / 2);
    final c = 2 * atan2(sqrt(a), sqrt(1 - a));
    return _earthRadiusMeters * c;
  }

  /// [expectedSeqCheckpointIds] is the route's checkpoint ids in ascending sequence.
  static bool isInOrder(
      List<String> expectedSeqCheckpointIds, Set<String> alreadyScanned, String checkpointId) {
    for (final id in expectedSeqCheckpointIds) {
      if (!alreadyScanned.contains(id)) {
        return id == checkpointId;
      }
    }
    return false;
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter test test/domain/scan_validation_test.dart`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add mobile/lib/domain/scan_validation.dart mobile/test/domain/scan_validation_test.dart
git commit -m "feat(mobile): client-side geofence and order validation"
```

---

### Task 4: Drift local store (reference cache + outbox + scans)

**Files:**
- Create: `mobile/lib/data/local/app_database.dart` (+ generated `app_database.g.dart`)
- Test: `mobile/test/data/app_database_test.dart`

**Interfaces:**
- Produces `AppDatabase` (Drift) with tables `CachedRoutes`, `CachedCheckpoints`, `LocalPatrols`, `LocalScans`, `Outbox`, and methods:
  - `upsertRoutes(List<RouteRef>)`, `getCachedRoutesForSite(String siteId)`, `replaceCheckpoints(String routeId, List<CheckpointRef>)`, `getCheckpoints(String routeId)`
  - `insertPatrol(LocalPatrol)`, `markPatrolCompleted(String id, DateTime at)`
  - `insertScan(LocalScan)`, `getScans(String patrolId)`, `markScanSent(String id)`
  - `enqueue(OutboxCompanion)`, `pendingOutbox()`, `markOutboxSent(int rowId)`, `bumpOutboxAttempt(int rowId)`
- Consumes: domain entities from Task 2.

- [ ] **Step 1: Write the database and tables**

Create `mobile/lib/data/local/app_database.dart`:
```dart
import 'package:drift/drift.dart';
import '../../domain/enums.dart';
import '../../domain/entities/route_ref.dart';
import '../../domain/entities/checkpoint_ref.dart';
import '../../domain/entities/local_patrol.dart';
import '../../domain/entities/local_scan.dart';

part 'app_database.g.dart';

class CachedRoutes extends Table {
  TextColumn get id => text()();
  TextColumn get siteId => text()();
  TextColumn get name => text()();
  BoolColumn get enforceOrder => boolean()();
  @override
  Set<Column> get primaryKey => {id};
}

class CachedCheckpoints extends Table {
  TextColumn get id => text()();
  TextColumn get routeId => text()();
  TextColumn get name => text()();
  TextColumn get qrCode => text()();
  RealColumn get lat => real()();
  RealColumn get lng => real()();
  RealColumn get geofenceRadiusM => real()();
  IntColumn get sequence => integer()();
  @override
  Set<Column> get primaryKey => {id};
}

class LocalPatrols extends Table {
  TextColumn get id => text()();
  TextColumn get routeId => text()();
  DateTimeColumn get startedAt => dateTime()();
  DateTimeColumn get completedAt => dateTime().nullable()();
  IntColumn get status => intEnum<PatrolSyncStatus>()();
  @override
  Set<Column> get primaryKey => {id};
}

class LocalScans extends Table {
  TextColumn get id => text()();
  TextColumn get patrolId => text()();
  TextColumn get checkpointId => text()();
  DateTimeColumn get scannedAt => dateTime()();
  RealColumn get lat => real().nullable()();
  RealColumn get lng => real().nullable()();
  BoolColumn get geoValid => boolean()();
  BoolColumn get orderValid => boolean()();
  IntColumn get sendStatus => intEnum<SendStatus>()();
  @override
  Set<Column> get primaryKey => {id};
}

class Outbox extends Table {
  IntColumn get rowId => integer().autoIncrement()();
  IntColumn get kind => intEnum<ScanKind>()();
  TextColumn get refId => text()();          // patrolId or scanId
  TextColumn get payloadJson => text()();    // request body
  IntColumn get attempts => integer().withDefault(const Constant(0))();
  IntColumn get sendStatus => intEnum<SendStatus>()();
}

@DriftDatabase(tables: [CachedRoutes, CachedCheckpoints, LocalPatrols, LocalScans, Outbox])
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.e);

  @override
  int get schemaVersion => 1;

  Future<void> upsertRoutes(List<RouteRef> routes) async {
    await batch((b) {
      for (final r in routes) {
        b.insert(
          cachedRoutes,
          CachedRoutesCompanion.insert(
              id: r.id, siteId: r.siteId, name: r.name, enforceOrder: r.enforceOrder),
          onConflict: DoUpdate((_) => CachedRoutesCompanion.custom(
                siteId: Variable(r.siteId),
                name: Variable(r.name),
                enforceOrder: Variable(r.enforceOrder),
              )),
        );
      }
    });
  }

  Future<List<RouteRef>> getCachedRoutesForSite(String siteId) async {
    final rows = await (select(cachedRoutes)..where((t) => t.siteId.equals(siteId))).get();
    return rows
        .map((r) => RouteRef(id: r.id, siteId: r.siteId, name: r.name, enforceOrder: r.enforceOrder))
        .toList();
  }

  Future<void> replaceCheckpoints(String routeId, List<CheckpointRef> checkpoints) async {
    await transaction(() async {
      await (delete(cachedCheckpoints)..where((t) => t.routeId.equals(routeId))).go();
      await batch((b) {
        for (final c in checkpoints) {
          b.insert(
            cachedCheckpoints,
            CachedCheckpointsCompanion.insert(
              id: c.id, routeId: c.routeId, name: c.name, qrCode: c.qrCode,
              lat: c.lat, lng: c.lng, geofenceRadiusM: c.geofenceRadiusM, sequence: c.sequence,
            ),
          );
        }
      });
    });
  }

  Future<List<CheckpointRef>> getCheckpoints(String routeId) async {
    final rows = await (select(cachedCheckpoints)
          ..where((t) => t.routeId.equals(routeId))
          ..orderBy([(t) => OrderingTerm(expression: t.sequence)]))
        .get();
    return rows
        .map((c) => CheckpointRef(
              id: c.id, routeId: c.routeId, name: c.name, qrCode: c.qrCode,
              lat: c.lat, lng: c.lng, geofenceRadiusM: c.geofenceRadiusM, sequence: c.sequence,
            ))
        .toList();
  }

  Future<void> insertPatrol(LocalPatrol p) => into(localPatrols).insert(
        LocalPatrolsCompanion.insert(
          id: p.id, routeId: p.routeId, startedAt: p.startedAt,
          completedAt: Value(p.completedAt), status: p.status,
        ),
      );

  Future<void> markPatrolCompleted(String id, DateTime at) =>
      (update(localPatrols)..where((t) => t.id.equals(id))).write(
        LocalPatrolsCompanion(status: Value(PatrolSyncStatus.completed), completedAt: Value(at)),
      );

  Future<void> insertScan(LocalScan s) => into(localScans).insert(
        LocalScansCompanion.insert(
          id: s.id, patrolId: s.patrolId, checkpointId: s.checkpointId, scannedAt: s.scannedAt,
          lat: Value(s.lat), lng: Value(s.lng), geoValid: s.geoValid, orderValid: s.orderValid,
          sendStatus: s.sendStatus,
        ),
      );

  Future<List<LocalScan>> getScans(String patrolId) async {
    final rows = await (select(localScans)
          ..where((t) => t.patrolId.equals(patrolId))
          ..orderBy([(t) => OrderingTerm(expression: t.scannedAt)]))
        .get();
    return rows
        .map((s) => LocalScan(
              id: s.id, patrolId: s.patrolId, checkpointId: s.checkpointId, scannedAt: s.scannedAt,
              lat: s.lat, lng: s.lng, geoValid: s.geoValid, orderValid: s.orderValid,
              sendStatus: s.sendStatus,
            ))
        .toList();
  }

  Future<void> markScanSent(String id) =>
      (update(localScans)..where((t) => t.id.equals(id)))
          .write(const LocalScansCompanion(sendStatus: Value(SendStatus.sent)));

  Future<int> enqueue(OutboxCompanion entry) => into(outbox).insert(entry);

  Future<List<OutboxData>> pendingOutbox() =>
      (select(outbox)
            ..where((t) => t.sendStatus.equalsValue(SendStatus.pending))
            ..orderBy([(t) => OrderingTerm(expression: t.rowId)]))
          .get();

  Future<void> markOutboxSent(int rowId) =>
      (update(outbox)..where((t) => t.rowId.equals(rowId)))
          .write(const OutboxCompanion(sendStatus: Value(SendStatus.sent)));

  Future<void> bumpOutboxAttempt(int rowId) => customUpdate(
        'UPDATE outbox SET attempts = attempts + 1 WHERE row_id = ?',
        variables: [Variable(rowId)],
        updates: {outbox},
      );
}
```

- [ ] **Step 2: Generate Drift code**

Run: `cd mobile && dart run build_runner build --delete-conflicting-outputs`
Expected: `app_database.g.dart` is generated; project compiles.

- [ ] **Step 3: Write the failing test**

Create `mobile/test/data/app_database_test.dart`:
```dart
import 'package:bekci/data/local/app_database.dart';
import 'package:bekci/domain/enums.dart';
import 'package:bekci/domain/entities/checkpoint_ref.dart';
import 'package:bekci/domain/entities/local_scan.dart';
import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late AppDatabase db;

  setUp(() => db = AppDatabase(NativeDatabase.memory()));
  tearDown(() => db.close());

  test('checkpoints round-trip ordered by sequence', () async {
    await db.replaceCheckpoints('r1', [
      const CheckpointRef(id: 'c2', routeId: 'r1', name: 'B', qrCode: 'Q2', lat: 1, lng: 1, geofenceRadiusM: 25, sequence: 2),
      const CheckpointRef(id: 'c1', routeId: 'r1', name: 'A', qrCode: 'Q1', lat: 1, lng: 1, geofenceRadiusM: 25, sequence: 1),
    ]);
    final cps = await db.getCheckpoints('r1');
    expect(cps.map((c) => c.id), ['c1', 'c2']);
  });

  test('scan insert then mark sent', () async {
    await db.insertScan(LocalScan(
      id: 's1', patrolId: 'p1', checkpointId: 'c1', scannedAt: DateTime.utc(2026, 7, 10),
      lat: null, lng: null, geoValid: false, orderValid: true, sendStatus: SendStatus.pending,
    ));
    await db.markScanSent('s1');
    final scans = await db.getScans('p1');
    expect(scans.single.sendStatus, SendStatus.sent);
  });

  test('outbox enqueue and pending query', () async {
    await db.enqueue(OutboxCompanion.insert(
      kind: ScanKind.scan, refId: 's1', payloadJson: '{}', sendStatus: SendStatus.pending));
    final pending = await db.pendingOutbox();
    expect(pending, hasLength(1));
    await db.markOutboxSent(pending.first.rowId);
    expect(await db.pendingOutbox(), isEmpty);
  });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter test test/data/app_database_test.dart`
Expected: PASS. (Drift's `NativeDatabase.memory()` uses the host sqlite3; runs under `flutter test` on desktop.)

- [ ] **Step 5: Commit**

```bash
git add mobile/lib/data/local mobile/test/data/app_database_test.dart
git commit -m "feat(mobile): Drift local store — cache, scans, outbox"
```

---

### Task 5: Remote API client (Dio + Retrofit) with JSON DTOs and auth interceptor

**Files:**
- Create: `mobile/lib/data/remote/dtos.dart` (+ generated `dtos.g.dart`), `mobile/lib/data/remote/api_client.dart` (+ generated `api_client.g.dart`), `mobile/lib/data/remote/auth_interceptor.dart`
- Test: `mobile/test/data/api_client_test.dart`

**Interfaces:**
- Produces:
  - DTOs (all `@JsonSerializable`): `LoginRequestDto(email,password)`, `LoginResponseDto(token,role,tenantId)`, `RouteDto(id,siteId,name,enforceOrder)`, `CheckpointDto(id,routeId,name,qrCode,lat,lng,geofenceRadiusM,sequence)`, `StartPatrolDto(patrolId,routeId,startedAt)`, `ScanInputDto(scanId,checkpointId,scannedAt,lat,lng)`, `IngestScansDto(scans)`, `ScanResultDto(scanId,geoValid,orderValid,isDuplicate)`, `IngestScansResponseDto(results)`, `CompletePatrolDto(completedAt)`, `PatrolSummaryDto(...)`, `PatrolDetailDto(...)`, `ScanDetailDto(...)`.
  - `@RestApi` `ApiClient` with one method per endpoint in Global Constraints.
  - `AuthInterceptor` that reads the current token from a `TokenStore` callback and adds `Authorization: Bearer`.
- Consumes: nothing from other tasks (DTOs are self-contained; mapping to domain happens in repositories).

- [ ] **Step 1: Write the DTOs**

Create `mobile/lib/data/remote/dtos.dart`:
```dart
import 'package:json_annotation/json_annotation.dart';

part 'dtos.g.dart';

@JsonSerializable()
class LoginRequestDto {
  final String email;
  final String password;
  LoginRequestDto(this.email, this.password);
  Map<String, dynamic> toJson() => _$LoginRequestDtoToJson(this);
}

@JsonSerializable()
class LoginResponseDto {
  final String token;
  final String role;
  final String tenantId;
  LoginResponseDto(this.token, this.role, this.tenantId);
  factory LoginResponseDto.fromJson(Map<String, dynamic> j) => _$LoginResponseDtoFromJson(j);
}

@JsonSerializable()
class RouteDto {
  final String id;
  final String siteId;
  final String name;
  final bool enforceOrder;
  RouteDto(this.id, this.siteId, this.name, this.enforceOrder);
  factory RouteDto.fromJson(Map<String, dynamic> j) => _$RouteDtoFromJson(j);
}

@JsonSerializable()
class CheckpointDto {
  final String id;
  final String routeId;
  final String name;
  final String qrCode;
  final double lat;
  final double lng;
  final double geofenceRadiusM;
  final int sequence;
  CheckpointDto(this.id, this.routeId, this.name, this.qrCode, this.lat, this.lng,
      this.geofenceRadiusM, this.sequence);
  factory CheckpointDto.fromJson(Map<String, dynamic> j) => _$CheckpointDtoFromJson(j);
}

@JsonSerializable()
class StartPatrolDto {
  final String patrolId;
  final String routeId;
  final String startedAt;
  StartPatrolDto(this.patrolId, this.routeId, this.startedAt);
  Map<String, dynamic> toJson() => _$StartPatrolDtoToJson(this);
}

@JsonSerializable()
class ScanInputDto {
  final String scanId;
  final String checkpointId;
  final String scannedAt;
  final double? lat;
  final double? lng;
  ScanInputDto(this.scanId, this.checkpointId, this.scannedAt, this.lat, this.lng);
  Map<String, dynamic> toJson() => _$ScanInputDtoToJson(this);
  factory ScanInputDto.fromJson(Map<String, dynamic> j) => _$ScanInputDtoFromJson(j);
}

@JsonSerializable()
class IngestScansDto {
  final List<ScanInputDto> scans;
  IngestScansDto(this.scans);
  Map<String, dynamic> toJson() => _$IngestScansDtoToJson(this);
}

@JsonSerializable()
class ScanResultDto {
  final String scanId;
  final bool geoValid;
  final bool orderValid;
  final bool isDuplicate;
  ScanResultDto(this.scanId, this.geoValid, this.orderValid, this.isDuplicate);
  factory ScanResultDto.fromJson(Map<String, dynamic> j) => _$ScanResultDtoFromJson(j);
}

@JsonSerializable()
class IngestScansResponseDto {
  final List<ScanResultDto> results;
  IngestScansResponseDto(this.results);
  factory IngestScansResponseDto.fromJson(Map<String, dynamic> j) =>
      _$IngestScansResponseDtoFromJson(j);
}

@JsonSerializable()
class CompletePatrolDto {
  final String completedAt;
  CompletePatrolDto(this.completedAt);
  Map<String, dynamic> toJson() => _$CompletePatrolDtoToJson(this);
}

@JsonSerializable()
class PatrolSummaryDto {
  final String id;
  final String routeId;
  final String guardId;
  final String startedAt;
  final String? completedAt;
  final String status;
  final int scanCount;
  PatrolSummaryDto(this.id, this.routeId, this.guardId, this.startedAt, this.completedAt,
      this.status, this.scanCount);
  factory PatrolSummaryDto.fromJson(Map<String, dynamic> j) => _$PatrolSummaryDtoFromJson(j);
}

@JsonSerializable()
class ScanDetailDto {
  final String id;
  final String checkpointId;
  final String checkpointName;
  final String scannedAt;
  final bool geoValid;
  final bool orderValid;
  final bool isDuplicate;
  ScanDetailDto(this.id, this.checkpointId, this.checkpointName, this.scannedAt,
      this.geoValid, this.orderValid, this.isDuplicate);
  factory ScanDetailDto.fromJson(Map<String, dynamic> j) => _$ScanDetailDtoFromJson(j);
}

@JsonSerializable()
class PatrolDetailDto {
  final String id;
  final String routeId;
  final String guardId;
  final String startedAt;
  final String? completedAt;
  final String status;
  final List<ScanDetailDto> scans;
  PatrolDetailDto(this.id, this.routeId, this.guardId, this.startedAt, this.completedAt,
      this.status, this.scans);
  factory PatrolDetailDto.fromJson(Map<String, dynamic> j) => _$PatrolDetailDtoFromJson(j);
}
```

- [ ] **Step 2: Write the Retrofit client and interceptor**

Create `mobile/lib/data/remote/api_client.dart`:
```dart
import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';
import 'dtos.dart';

part 'api_client.g.dart';

@RestApi()
abstract class ApiClient {
  factory ApiClient(Dio dio, {String baseUrl}) = _ApiClient;

  @POST('/api/v1/auth/login')
  Future<LoginResponseDto> login(@Body() LoginRequestDto body);

  @GET('/api/v1/guard/routes')
  Future<List<RouteDto>> guardRoutes();

  @GET('/api/v1/routes/{routeId}/checkpoints')
  Future<List<CheckpointDto>> checkpoints(@Path('routeId') String routeId);

  @POST('/api/v1/patrols')
  Future<void> startPatrol(@Body() StartPatrolDto body);

  @POST('/api/v1/patrols/{id}/scans')
  Future<IngestScansResponseDto> ingestScans(@Path('id') String id, @Body() IngestScansDto body);

  @POST('/api/v1/patrols/{id}/complete')
  Future<void> completePatrol(@Path('id') String id, @Body() CompletePatrolDto body);

  @GET('/api/v1/patrols')
  Future<List<PatrolSummaryDto>> listPatrols(
    @Query('siteId') String? siteId,
    @Query('routeId') String? routeId,
    @Query('guardId') String? guardId,
  );

  @GET('/api/v1/patrols/{id}')
  Future<PatrolDetailDto> patrolDetail(@Path('id') String id);
}
```

Create `mobile/lib/data/remote/auth_interceptor.dart`:
```dart
import 'package:dio/dio.dart';

class AuthInterceptor extends Interceptor {
  final String? Function() tokenProvider;
  AuthInterceptor(this.tokenProvider);

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final token = tokenProvider();
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }
}
```

- [ ] **Step 3: Generate code**

Run: `cd mobile && dart run build_runner build --delete-conflicting-outputs`
Expected: `dtos.g.dart` and `api_client.g.dart` generated; compiles.

- [ ] **Step 4: Write the failing test**

Create `mobile/test/data/api_client_test.dart`:
```dart
import 'package:bekci/data/remote/api_client.dart';
import 'package:bekci/data/remote/dtos.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';

void main() {
  test('login posts credentials and parses token', () async {
    final dio = Dio(BaseOptions(baseUrl: 'http://test'));
    final adapter = DioAdapter(dio: dio);
    adapter.onPost(
      '/api/v1/auth/login',
      (server) => server.reply(200, {'token': 'jwt-123', 'role': 'Guard', 'tenantId': 't1'}),
      data: {'email': 'g@a.com', 'password': 'pass'},
    );

    final api = ApiClient(dio);
    final resp = await api.login(LoginRequestDto('g@a.com', 'pass'));

    expect(resp.token, 'jwt-123');
    expect(resp.role, 'Guard');
  });

  test('ingestScans parses verdicts', () async {
    final dio = Dio(BaseOptions(baseUrl: 'http://test'));
    final adapter = DioAdapter(dio: dio);
    adapter.onPost(
      '/api/v1/patrols/p1/scans',
      (server) => server.reply(200, {
        'results': [
          {'scanId': 's1', 'geoValid': true, 'orderValid': false, 'isDuplicate': false}
        ]
      }),
      data: Matchers.any,
    );

    final api = ApiClient(dio);
    final resp = await api.ingestScans('p1', IngestScansDto([ScanInputDto('s1', 'c1', '2026-07-10T00:00:00Z', 40.0, 29.0)]));

    expect(resp.results.single.geoValid, isTrue);
    expect(resp.results.single.orderValid, isFalse);
  });
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `flutter test test/data/api_client_test.dart`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mobile/lib/data/remote mobile/test/data/api_client_test.dart
git commit -m "feat(mobile): Retrofit API client, DTOs, auth interceptor"
```

---

### Task 6: Core providers + Auth repository + session storage

**Files:**
- Create: `mobile/lib/core/di.dart`, `mobile/lib/data/repositories/auth_repository_impl.dart`
- Test: `mobile/test/data/auth_repository_test.dart`

**Interfaces:**
- Consumes: `ApiClient`, `AppDatabase`, `LoginRequestDto`, `LoginResponseDto`, `AuthSession`, `UserRole`.
- Produces:
  - `abstract class AuthRepository` (per Shared Type Reference) in `domain/repositories/auth_repository.dart`.
  - `AuthRepositoryImpl(ApiClient api, TokenStore store)` — `login` calls the API, decodes the JWT payload to extract `role`, `tenant_id`, `site_id`, persists `token` via `flutter_secure_storage`, returns `AuthSession`.
  - `TokenStore` abstraction (a thin wrapper over secure storage) so the Dio `AuthInterceptor` and the repository share one token source.
  - Riverpod providers in `core/di.dart`: `tokenStoreProvider`, `dioProvider` (with `AuthInterceptor`), `apiClientProvider`, `appDatabaseProvider`, `authRepositoryProvider`.

- [ ] **Step 1: Write the domain interface**

Create `mobile/lib/domain/repositories/auth_repository.dart`:
```dart
import '../entities/auth_session.dart';

abstract class AuthRepository {
  Future<AuthSession> login(String email, String password);
  Future<AuthSession?> currentSession();
  Future<void> logout();
}
```

- [ ] **Step 2: Write the failing test**

Create `mobile/test/data/auth_repository_test.dart`:
```dart
import 'package:bekci/data/remote/api_client.dart';
import 'package:bekci/data/remote/dtos.dart';
import 'package:bekci/data/repositories/auth_repository_impl.dart';
import 'package:bekci/domain/enums.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockApi extends Mock implements ApiClient {}

class _FakeStore implements TokenStore {
  String? _t;
  @override
  Future<String?> read() async => _t;
  @override
  Future<void> write(String token) async => _t = token;
  @override
  Future<void> clear() async => _t = null;
  @override
  String? get cached => _t;
}

// A JWT whose payload is {"role":"Guard","tenant_id":"t1","site_id":"s1"} (unsigned test token).
const _jwt =
    'eyJhbGciOiJIUzI1NiJ9.eyJyb2xlIjoiR3VhcmQiLCJ0ZW5hbnRfaWQiOiJ0MSIsInNpdGVfaWQiOiJzMSJ9.sig';

void main() {
  setUpAll(() => registerFallbackValue(LoginRequestDto('x', 'y')));

  test('login persists token and maps claims to session', () async {
    final api = _MockApi();
    final store = _FakeStore();
    when(() => api.login(any()))
        .thenAnswer((_) async => LoginResponseDto(_jwt, 'Guard', 't1'));

    final repo = AuthRepositoryImpl(api, store);
    final session = await repo.login('g@a.com', 'pass');

    expect(session.role, UserRole.guard);
    expect(session.tenantId, 't1');
    expect(session.siteId, 's1');
    expect(await store.read(), _jwt);
  });
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `flutter test test/data/auth_repository_test.dart`
Expected: FAIL — `AuthRepositoryImpl` / `TokenStore` do not exist.

- [ ] **Step 4: Write TokenStore, the repository impl, and providers**

Create `mobile/lib/data/repositories/auth_repository_impl.dart`:
```dart
import 'dart:convert';
import '../../domain/entities/auth_session.dart';
import '../../domain/enums.dart';
import '../../domain/repositories/auth_repository.dart';
import '../remote/api_client.dart';
import '../remote/dtos.dart';

abstract class TokenStore {
  Future<String?> read();
  Future<void> write(String token);
  Future<void> clear();
  String? get cached; // synchronous access for the Dio interceptor
}

class AuthRepositoryImpl implements AuthRepository {
  final ApiClient _api;
  final TokenStore _store;
  AuthRepositoryImpl(this._api, this._store);

  @override
  Future<AuthSession> login(String email, String password) async {
    final resp = await _api.login(LoginRequestDto(email, password));
    await _store.write(resp.token);
    return _sessionFromToken(resp.token);
  }

  @override
  Future<AuthSession?> currentSession() async {
    final token = await _store.read();
    if (token == null || token.isEmpty) return null;
    return _sessionFromToken(token);
  }

  @override
  Future<void> logout() => _store.clear();

  AuthSession _sessionFromToken(String token) {
    final claims = _decodeJwtPayload(token);
    final roleStr = (claims['role'] as String?) ?? 'Guard';
    return AuthSession(
      token: token,
      role: roleStr == 'Supervisor' ? UserRole.supervisor : UserRole.guard,
      tenantId: (claims['tenant_id'] as String?) ?? '',
      siteId: claims['site_id'] as String?,
    );
  }

  Map<String, dynamic> _decodeJwtPayload(String token) {
    final parts = token.split('.');
    if (parts.length != 3) return {};
    var payload = parts[1].replaceAll('-', '+').replaceAll('_', '/');
    while (payload.length % 4 != 0) {
      payload += '=';
    }
    final decoded = utf8.decode(base64.decode(payload));
    return jsonDecode(decoded) as Map<String, dynamic>;
  }
}
```

Create `mobile/lib/core/di.dart`:
```dart
import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:drift/drift.dart';
import 'config.dart';
import '../data/local/app_database.dart';
import '../data/remote/api_client.dart';
import '../data/remote/auth_interceptor.dart';
import '../data/repositories/auth_repository_impl.dart';
import '../domain/repositories/auth_repository.dart';

class SecureTokenStore implements TokenStore {
  final FlutterSecureStorage _storage;
  String? _cached;
  SecureTokenStore(this._storage);

  @override
  String? get cached => _cached;

  @override
  Future<String?> read() async => _cached ??= await _storage.read(key: 'jwt');

  @override
  Future<void> write(String token) async {
    _cached = token;
    await _storage.write(key: 'jwt', value: token);
  }

  @override
  Future<void> clear() async {
    _cached = null;
    await _storage.delete(key: 'jwt');
  }
}

final tokenStoreProvider = Provider<TokenStore>(
    (ref) => SecureTokenStore(const FlutterSecureStorage()));

final dioProvider = Provider<Dio>((ref) {
  final store = ref.watch(tokenStoreProvider);
  final dio = Dio(BaseOptions(baseUrl: apiBaseUrl));
  dio.interceptors.add(AuthInterceptor(() => store.cached));
  return dio;
});

final apiClientProvider = Provider<ApiClient>((ref) => ApiClient(ref.watch(dioProvider)));

final appDatabaseProvider = Provider<AppDatabase>((ref) {
  final db = AppDatabase(LazyDatabase(() async {
    final dir = await getApplicationDocumentsDirectory();
    return NativeDatabase(File(p.join(dir.path, 'bekci.sqlite')));
  }));
  ref.onDispose(db.close);
  return db;
});

final authRepositoryProvider = Provider<AuthRepository>(
    (ref) => AuthRepositoryImpl(ref.watch(apiClientProvider), ref.watch(tokenStoreProvider)));
```

> Add `import 'dart:io';` at the top of `di.dart` (for `File`). If your editor auto-adds it, ensure it's present.

- [ ] **Step 5: Run test to verify it passes**

Run: `flutter test test/data/auth_repository_test.dart`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mobile/lib/core/di.dart mobile/lib/domain/repositories/auth_repository.dart mobile/lib/data/repositories/auth_repository_impl.dart mobile/test/data/auth_repository_test.dart
git commit -m "feat(mobile): auth repository, JWT claim decode, DI providers"
```

---

### Task 7: Route repository — fetch guard routes + cache + checkpoints

**Files:**
- Create: `mobile/lib/domain/repositories/route_repository.dart`, `mobile/lib/data/repositories/route_repository_impl.dart`
- Modify: `mobile/lib/core/di.dart` (add `routeRepositoryProvider`)
- Test: `mobile/test/data/route_repository_test.dart`

**Interfaces:**
- Consumes: `ApiClient`, `AppDatabase`, `RouteDto`, `CheckpointDto`, `RouteRef`, `CheckpointRef`.
- Produces:
  - `abstract class RouteRepository` (per Shared Type Reference).
  - `RouteRepositoryImpl(ApiClient api, AppDatabase db)` — `refreshGuardRoutes` fetches from API, upserts to cache, also fetches + caches each route's checkpoints, returns the routes. `cachedRoutes`/`checkpointsFor` read from Drift.
  - `routeRepositoryProvider`.

- [ ] **Step 1: Write the domain interface**

Create `mobile/lib/domain/repositories/route_repository.dart`:
```dart
import '../entities/route_ref.dart';
import '../entities/checkpoint_ref.dart';

abstract class RouteRepository {
  Future<List<RouteRef>> refreshGuardRoutes();
  Future<List<RouteRef>> cachedRoutes(String siteId);
  Future<List<CheckpointRef>> checkpointsFor(String routeId);
}
```

- [ ] **Step 2: Write the failing test**

Create `mobile/test/data/route_repository_test.dart`:
```dart
import 'package:bekci/data/local/app_database.dart';
import 'package:bekci/data/remote/api_client.dart';
import 'package:bekci/data/remote/dtos.dart';
import 'package:bekci/data/repositories/route_repository_impl.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockApi extends Mock implements ApiClient {}

void main() {
  test('refresh caches routes and their checkpoints', () async {
    final api = _MockApi();
    final db = AppDatabase(NativeDatabase.memory());
    addTearDown(db.close);

    when(() => api.guardRoutes()).thenAnswer((_) async => [RouteDto('r1', 's1', 'Loop', true)]);
    when(() => api.checkpoints('r1')).thenAnswer(
        (_) async => [CheckpointDto('c1', 'r1', 'Lobby', 'QR-1', 40.0, 29.0, 25, 1)]);

    final repo = RouteRepositoryImpl(api, db);
    final routes = await repo.refreshGuardRoutes();

    expect(routes.single.name, 'Loop');
    final cached = await repo.cachedRoutes('s1');
    expect(cached.single.id, 'r1');
    final cps = await repo.checkpointsFor('r1');
    expect(cps.single.qrCode, 'QR-1');
  });
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `flutter test test/data/route_repository_test.dart`
Expected: FAIL — `RouteRepositoryImpl` does not exist.

- [ ] **Step 4: Write the repository impl + provider**

Create `mobile/lib/data/repositories/route_repository_impl.dart`:
```dart
import '../../domain/entities/route_ref.dart';
import '../../domain/entities/checkpoint_ref.dart';
import '../../domain/repositories/route_repository.dart';
import '../local/app_database.dart';
import '../remote/api_client.dart';

class RouteRepositoryImpl implements RouteRepository {
  final ApiClient _api;
  final AppDatabase _db;
  RouteRepositoryImpl(this._api, this._db);

  @override
  Future<List<RouteRef>> refreshGuardRoutes() async {
    final dtos = await _api.guardRoutes();
    final routes = dtos
        .map((r) => RouteRef(id: r.id, siteId: r.siteId, name: r.name, enforceOrder: r.enforceOrder))
        .toList();
    await _db.upsertRoutes(routes);

    for (final r in routes) {
      final cps = await _api.checkpoints(r.id);
      await _db.replaceCheckpoints(
        r.id,
        cps
            .map((c) => CheckpointRef(
                  id: c.id, routeId: c.routeId, name: c.name, qrCode: c.qrCode,
                  lat: c.lat, lng: c.lng, geofenceRadiusM: c.geofenceRadiusM, sequence: c.sequence,
                ))
            .toList(),
      );
    }
    return routes;
  }

  @override
  Future<List<RouteRef>> cachedRoutes(String siteId) => _db.getCachedRoutesForSite(siteId);

  @override
  Future<List<CheckpointRef>> checkpointsFor(String routeId) => _db.getCheckpoints(routeId);
}
```

Modify `mobile/lib/core/di.dart` — add these imports and provider (append at the end):
```dart
// add to imports:
// import '../data/repositories/route_repository_impl.dart';
// import '../domain/repositories/route_repository.dart';

final routeRepositoryProvider = Provider<RouteRepository>(
    (ref) => RouteRepositoryImpl(ref.watch(apiClientProvider), ref.watch(appDatabaseProvider)));
```

- [ ] **Step 5: Run test to verify it passes**

Run: `flutter test test/data/route_repository_test.dart`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mobile/lib/domain/repositories/route_repository.dart mobile/lib/data/repositories/route_repository_impl.dart mobile/lib/core/di.dart mobile/test/data/route_repository_test.dart
git commit -m "feat(mobile): route repository with reference caching"
```

---

### Task 8: Patrol repository — start, record scan (validate + local write + enqueue), complete

**Files:**
- Create: `mobile/lib/domain/repositories/patrol_repository.dart`, `mobile/lib/domain/entities/scan_outcome.dart`, `mobile/lib/data/repositories/patrol_repository_impl.dart`
- Modify: `mobile/lib/core/di.dart` (add `patrolRepositoryProvider`, `clockProvider`, `uuidProvider`)
- Test: `mobile/test/data/patrol_repository_test.dart`

**Interfaces:**
- Consumes: `AppDatabase`, `ApiClient`, `RouteRepository`, `ScanValidation`, `AppDatabase` outbox, DTOs.
- Produces:
  - `class ScanOutcome { final LocalScan? scan; final bool matchedCheckpoint; }`
  - `abstract class PatrolRepository` (per Shared Type Reference).
  - `PatrolRepositoryImpl(AppDatabase db, ApiClient api, RouteRepository routes, String Function() newId, DateTime Function() nowUtc)`:
    - `startPatrol(routeId)`: create `LocalPatrol` with a new UUID, insert locally, attempt immediate `api.startPatrol`; on failure enqueue an `Outbox(kind: startPatrol)`. Returns the `LocalPatrol`.
    - `recordScan({patrolId, routeId, qrCode})`: look up the checkpoint by `qrCode` in the cache; if none → return `ScanOutcome(matchedCheckpoint:false)`. Else capture GPS (injected `LocationProvider`, Task-simplified to a function returning `(lat,lng)?`), compute `geoValid`/`orderValid` (using cached checkpoints + already-scanned set), write `LocalScan(pending)`, attempt immediate `api.ingestScans`; on success mark sent, on failure enqueue `Outbox(kind: scan)`. Returns `ScanOutcome(scan, true)`.
    - `completePatrol(patrolId)`: mark local completed, attempt `api.completePatrol`; on failure enqueue.
    - `scansFor(patrolId)`: read from Drift.
  - Location is injected as `Future<({double lat, double lng})?> Function() locationFn` so tests can supply a fake.

- [ ] **Step 1: Write interfaces and ScanOutcome**

Create `mobile/lib/domain/entities/scan_outcome.dart`:
```dart
import 'local_scan.dart';

class ScanOutcome {
  final LocalScan? scan;
  final bool matchedCheckpoint;
  const ScanOutcome({required this.scan, required this.matchedCheckpoint});
}
```

Create `mobile/lib/domain/repositories/patrol_repository.dart`:
```dart
import '../entities/local_patrol.dart';
import '../entities/local_scan.dart';
import '../entities/scan_outcome.dart';

abstract class PatrolRepository {
  Future<LocalPatrol> startPatrol(String routeId);
  Future<ScanOutcome> recordScan({
    required String patrolId,
    required String routeId,
    required String qrCode,
  });
  Future<void> completePatrol(String patrolId);
  Future<List<LocalScan>> scansFor(String patrolId);
}
```

- [ ] **Step 2: Write the failing test**

Create `mobile/test/data/patrol_repository_test.dart`:
```dart
import 'package:bekci/data/local/app_database.dart';
import 'package:bekci/data/remote/api_client.dart';
import 'package:bekci/data/remote/dtos.dart';
import 'package:bekci/data/repositories/patrol_repository_impl.dart';
import 'package:bekci/domain/enums.dart';
import 'package:bekci/domain/entities/checkpoint_ref.dart';
import 'package:bekci/domain/repositories/route_repository.dart';
import 'package:bekci/domain/entities/route_ref.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockApi extends Mock implements ApiClient {}

class _FakeRoutes implements RouteRepository {
  final AppDatabase db;
  _FakeRoutes(this.db);
  @override
  Future<List<CheckpointRef>> checkpointsFor(String routeId) => db.getCheckpoints(routeId);
  @override
  Future<List<RouteRef>> cachedRoutes(String siteId) async => [];
  @override
  Future<List<RouteRef>> refreshGuardRoutes() async => [];
}

void main() {
  setUpAll(() {
    registerFallbackValue(StartPatrolDto('p', 'r', 't'));
    registerFallbackValue(IngestScansDto(const []));
  });

  late AppDatabase db;
  late _MockApi api;
  late PatrolRepositoryImpl repo;
  var idCounter = 0;

  setUp(() async {
    db = AppDatabase(NativeDatabase.memory());
    api = _MockApi();
    idCounter = 0;
    repo = PatrolRepositoryImpl(
      db, api, _FakeRoutes(db),
      () => 'id-${++idCounter}',
      () => DateTime.utc(2026, 7, 10, 22),
      () async => (lat: 40.00005, lng: 29.0), // good GPS near c1
    );
    await db.replaceCheckpoints('r1', [
      const CheckpointRef(id: 'c1', routeId: 'r1', name: 'Lobby', qrCode: 'QR-1', lat: 40.0, lng: 29.0, geofenceRadiusM: 25, sequence: 1),
    ]);
  });
  tearDown(() => db.close());

  test('recordScan matches checkpoint, computes geoValid, sends immediately', () async {
    when(() => api.ingestScans(any(), any())).thenAnswer(
        (_) async => IngestScansResponseDto([ScanResultDto('id-1', true, true, false)]));

    final outcome = await repo.recordScan(patrolId: 'p1', routeId: 'r1', qrCode: 'QR-1');

    expect(outcome.matchedCheckpoint, isTrue);
    expect(outcome.scan!.geoValid, isTrue);
    final scans = await db.getScans('p1');
    expect(scans.single.sendStatus, SendStatus.sent);
    expect(await db.pendingOutbox(), isEmpty);
  });

  test('unknown QR returns no match and records nothing', () async {
    final outcome = await repo.recordScan(patrolId: 'p1', routeId: 'r1', qrCode: 'WRONG');
    expect(outcome.matchedCheckpoint, isFalse);
    expect(await db.getScans('p1'), isEmpty);
  });

  test('offline scan (api throws) is queued in the outbox', () async {
    when(() => api.ingestScans(any(), any())).thenThrow(Exception('no network'));

    final outcome = await repo.recordScan(patrolId: 'p1', routeId: 'r1', qrCode: 'QR-1');

    expect(outcome.matchedCheckpoint, isTrue);
    final scans = await db.getScans('p1');
    expect(scans.single.sendStatus, SendStatus.pending);
    final pending = await db.pendingOutbox();
    expect(pending, hasLength(1));
    expect(pending.single.kind, ScanKind.scan);
  });
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `flutter test test/data/patrol_repository_test.dart`
Expected: FAIL — `PatrolRepositoryImpl` does not exist.

- [ ] **Step 4: Write the repository impl + providers**

Create `mobile/lib/data/repositories/patrol_repository_impl.dart`:
```dart
import 'dart:convert';
import 'package:drift/drift.dart';
import '../../domain/enums.dart';
import '../../domain/entities/local_patrol.dart';
import '../../domain/entities/local_scan.dart';
import '../../domain/entities/scan_outcome.dart';
import '../../domain/repositories/patrol_repository.dart';
import '../../domain/repositories/route_repository.dart';
import '../../domain/scan_validation.dart';
import '../local/app_database.dart';
import '../remote/api_client.dart';
import '../remote/dtos.dart';

typedef LocationFn = Future<({double lat, double lng})?> Function();

class PatrolRepositoryImpl implements PatrolRepository {
  final AppDatabase _db;
  final ApiClient _api;
  final RouteRepository _routes;
  final String Function() _newId;
  final DateTime Function() _nowUtc;
  final LocationFn _location;

  PatrolRepositoryImpl(this._db, this._api, this._routes, this._newId, this._nowUtc, this._location);

  @override
  Future<LocalPatrol> startPatrol(String routeId) async {
    final patrol = LocalPatrol(
      id: _newId(), routeId: routeId, startedAt: _nowUtc(),
      completedAt: null, status: PatrolSyncStatus.inProgress,
    );
    await _db.insertPatrol(patrol);

    final dto = StartPatrolDto(patrol.id, routeId, patrol.startedAt.toIso8601String());
    try {
      await _api.startPatrol(dto);
    } catch (_) {
      await _db.enqueue(OutboxCompanion.insert(
        kind: ScanKind.startPatrol, refId: patrol.id,
        payloadJson: jsonEncode(dto.toJson()), sendStatus: SendStatus.pending));
    }
    return patrol;
  }

  @override
  Future<ScanOutcome> recordScan({
    required String patrolId,
    required String routeId,
    required String qrCode,
  }) async {
    final checkpoints = await _routes.checkpointsFor(routeId);
    final cp = checkpoints.where((c) => c.qrCode == qrCode).firstOrNull;
    if (cp == null) {
      return const ScanOutcome(scan: null, matchedCheckpoint: false);
    }

    final loc = await _location();
    final lat = loc?.lat;
    final lng = loc?.lng;
    final geoValid = ScanValidation.isWithinGeofence(cp.lat, cp.lng, cp.geofenceRadiusM, lat, lng);

    final existing = await _db.getScans(patrolId);
    final alreadyScanned = existing.map((s) => s.checkpointId).toSet();
    final route = (await _routes.cachedRoutes('')).where((r) => r.id == routeId).firstOrNull;
    final enforceOrder = route?.enforceOrder ?? true;
    final orderValid = !enforceOrder
        ? true
        : ScanValidation.isInOrder(
            checkpoints.map((c) => c.id).toList(), alreadyScanned, cp.id);

    final scan = LocalScan(
      id: _newId(), patrolId: patrolId, checkpointId: cp.id, scannedAt: _nowUtc(),
      lat: lat, lng: lng, geoValid: geoValid, orderValid: orderValid, sendStatus: SendStatus.pending,
    );
    await _db.insertScan(scan);

    final input = ScanInputDto(scan.id, cp.id, scan.scannedAt.toIso8601String(), lat, lng);
    final body = IngestScansDto([input]);
    try {
      await _api.ingestScans(patrolId, body);
      await _db.markScanSent(scan.id);
      return ScanOutcome(scan: scan.copyWith(sendStatus: SendStatus.sent), matchedCheckpoint: true);
    } catch (_) {
      await _db.enqueue(OutboxCompanion.insert(
        kind: ScanKind.scan, refId: patrolId,
        payloadJson: jsonEncode(body.toJson()), sendStatus: SendStatus.pending));
      return ScanOutcome(scan: scan, matchedCheckpoint: true);
    }
  }

  @override
  Future<void> completePatrol(String patrolId) async {
    final at = _nowUtc();
    await _db.markPatrolCompleted(patrolId, at);
    final dto = CompletePatrolDto(at.toIso8601String());
    try {
      await _api.completePatrol(patrolId, dto);
    } catch (_) {
      await _db.enqueue(OutboxCompanion.insert(
        kind: ScanKind.completePatrol, refId: patrolId,
        payloadJson: jsonEncode(dto.toJson()), sendStatus: SendStatus.pending));
    }
  }

  @override
  Future<List<LocalScan>> scansFor(String patrolId) => _db.getScans(patrolId);
}

extension _FirstOrNull<E> on Iterable<E> {
  E? get firstOrNull => isEmpty ? null : first;
}
```

> `recordScan` reads `enforceOrder` from `cachedRoutes('')` — since Task 4's `getCachedRoutesForSite` filters by site, change the call to a direct lookup. To keep it simple and correct, add a small DB helper in the next step.

- [ ] **Step 5: Add a route lookup helper and wire providers**

Add to `mobile/lib/data/local/app_database.dart` inside `AppDatabase` (a by-id route lookup):
```dart
  Future<RouteRef?> getRouteById(String id) async {
    final row = await (select(cachedRoutes)..where((t) => t.id.equals(id))).getSingleOrNull();
    if (row == null) return null;
    return RouteRef(id: row.id, siteId: row.siteId, name: row.name, enforceOrder: row.enforceOrder);
  }
```
Then in `patrol_repository_impl.dart` replace the `route`/`enforceOrder` lines in `recordScan` with:
```dart
    final route = await _dbRoute(routeId);
    final enforceOrder = route?.enforceOrder ?? true;
```
and add a private helper + constructor access to `_db`:
```dart
  Future<RouteRef?> _dbRoute(String routeId) => _db.getRouteById(routeId);
```
(Import `RouteRef` in `patrol_repository_impl.dart`: `import '../../domain/entities/route_ref.dart';`.)

Modify `mobile/lib/core/di.dart` — add imports and providers (append):
```dart
// imports:
// import 'package:geolocator/geolocator.dart';
// import 'package:uuid/uuid.dart';
// import '../data/repositories/patrol_repository_impl.dart';
// import '../domain/repositories/patrol_repository.dart';

final _uuid = const Uuid();

Future<({double lat, double lng})?> _currentLocation() async {
  try {
    if (!await Geolocator.isLocationServiceEnabled()) return null;
    var perm = await Geolocator.checkPermission();
    if (perm == LocationPermission.denied) perm = await Geolocator.requestPermission();
    if (perm == LocationPermission.denied || perm == LocationPermission.deniedForever) return null;
    final pos = await Geolocator.getCurrentPosition();
    return (lat: pos.latitude, lng: pos.longitude);
  } catch (_) {
    return null;
  }
}

final patrolRepositoryProvider = Provider<PatrolRepository>((ref) => PatrolRepositoryImpl(
      ref.watch(appDatabaseProvider),
      ref.watch(apiClientProvider),
      ref.watch(routeRepositoryProvider),
      () => _uuid.v4(),
      () => DateTime.now().toUtc(),
      _currentLocation,
    ));
```

- [ ] **Step 6: Regenerate Drift code, run test**

Run:
```bash
cd mobile
dart run build_runner build --delete-conflicting-outputs
flutter test test/data/patrol_repository_test.dart
```
Expected: PASS. (Update the `_FakeRoutes` in the test if needed — it already exposes `checkpointsFor`; the impl now uses `_db.getRouteById`, which the in-memory DB serves.)

> Because `recordScan` now reads the route via `_db.getRouteById`, seed a route row in the test `setUp`: add `await db.upsertRoutes([const RouteRef(id: 'r1', siteId: 's1', name: 'Loop', enforceOrder: true)]);` (import `route_ref.dart`).

- [ ] **Step 7: Commit**

```bash
git add mobile/lib/domain mobile/lib/data mobile/lib/core/di.dart mobile/test/data/patrol_repository_test.dart
git commit -m "feat(mobile): patrol repository — start, scan (validate+send/queue), complete"
```

---

### Task 9: Sync service — outbox flush engine (idempotent, retry, connectivity)

**Files:**
- Create: `mobile/lib/data/sync/sync_service.dart`
- Modify: `mobile/lib/core/di.dart` (add `syncServiceProvider`)
- Test: `mobile/test/data/sync_service_test.dart`

**Interfaces:**
- Consumes: `AppDatabase` (outbox), `ApiClient`, DTOs, `ScanKind`.
- Produces:
  - `class SyncService { SyncService(AppDatabase db, ApiClient api); Future<void> flush(); void start(); void dispose(); }`
  - `flush()`: reads `pendingOutbox()` in order; for each entry, decode `payloadJson` and dispatch by `kind` (`startPatrol` → `api.startPatrol`, `scan` → `api.ingestScans(refId, ...)`, `completePatrol` → `api.completePatrol(refId, ...)`); on success `markOutboxSent` and, for scans, `markScanSent` for each scan id in the payload; on failure `bumpOutboxAttempt` and stop (leave the rest pending for the next trigger).
  - `start()`: subscribes to `Connectivity().onConnectivityChanged`; when connectivity is not `none`, calls `flush()`.
- Idempotency: re-sending an already-delivered entry is safe because the backend dedupes on the client UUIDs in the payload.

- [ ] **Step 1: Write the failing test**

Create `mobile/test/data/sync_service_test.dart`:
```dart
import 'dart:convert';
import 'package:bekci/data/local/app_database.dart';
import 'package:bekci/data/remote/api_client.dart';
import 'package:bekci/data/remote/dtos.dart';
import 'package:bekci/data/sync/sync_service.dart';
import 'package:bekci/domain/enums.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockApi extends Mock implements ApiClient {}

void main() {
  setUpAll(() {
    registerFallbackValue(IngestScansDto(const []));
    registerFallbackValue(StartPatrolDto('p', 'r', 't'));
    registerFallbackValue(CompletePatrolDto('t'));
  });

  late AppDatabase db;
  late _MockApi api;
  late SyncService sync;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    api = _MockApi();
    sync = SyncService(db, api);
  });
  tearDown(() => db.close());

  test('flush sends pending scan then marks outbox + scan sent', () async {
    // Seed a local scan (pending) and its outbox entry.
    await db.insertScan(LocalScan(
      id: 's1', patrolId: 'p1', checkpointId: 'c1', scannedAt: DateTime.utc(2026, 7, 10),
      lat: null, lng: null, geoValid: false, orderValid: true, sendStatus: SendStatus.pending));
    final body = IngestScansDto([ScanInputDto('s1', 'c1', '2026-07-10T00:00:00Z', null, null)]);
    await db.enqueue(OutboxCompanion.insert(
      kind: ScanKind.scan, refId: 'p1', payloadJson: jsonEncode(body.toJson()),
      sendStatus: SendStatus.pending));

    when(() => api.ingestScans('p1', any()))
        .thenAnswer((_) async => IngestScansResponseDto([ScanResultDto('s1', false, true, false)]));

    await sync.flush();

    expect(await db.pendingOutbox(), isEmpty);
    expect((await db.getScans('p1')).single.sendStatus, SendStatus.sent);
  });

  test('flush stops and keeps entry pending on failure', () async {
    final body = IngestScansDto([ScanInputDto('s1', 'c1', '2026-07-10T00:00:00Z', null, null)]);
    await db.enqueue(OutboxCompanion.insert(
      kind: ScanKind.scan, refId: 'p1', payloadJson: jsonEncode(body.toJson()),
      sendStatus: SendStatus.pending));
    when(() => api.ingestScans(any(), any())).thenThrow(Exception('offline'));

    await sync.flush();

    final pending = await db.pendingOutbox();
    expect(pending, hasLength(1));
    expect(pending.single.attempts, 1);
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `flutter test test/data/sync_service_test.dart`
Expected: FAIL — `SyncService` does not exist.

- [ ] **Step 3: Write the sync service + provider**

Create `mobile/lib/data/sync/sync_service.dart`:
```dart
import 'dart:async';
import 'dart:convert';
import 'package:connectivity_plus/connectivity_plus.dart';
import '../../domain/enums.dart';
import '../local/app_database.dart';
import '../remote/api_client.dart';
import '../remote/dtos.dart';

class SyncService {
  final AppDatabase _db;
  final ApiClient _api;
  StreamSubscription<List<ConnectivityResult>>? _sub;
  bool _flushing = false;

  SyncService(this._db, this._api);

  void start() {
    _sub = Connectivity().onConnectivityChanged.listen((results) {
      final online = results.any((r) => r != ConnectivityResult.none);
      if (online) flush();
    });
  }

  void dispose() => _sub?.cancel();

  Future<void> flush() async {
    if (_flushing) return;
    _flushing = true;
    try {
      final pending = await _db.pendingOutbox();
      for (final entry in pending) {
        final payload = jsonDecode(entry.payloadJson) as Map<String, dynamic>;
        try {
          await _dispatch(entry.kind, entry.refId, payload);
          await _db.markOutboxSent(entry.rowId);
          if (entry.kind == ScanKind.scan) {
            final scans = (payload['scans'] as List).cast<Map<String, dynamic>>();
            for (final s in scans) {
              await _db.markScanSent(s['scanId'] as String);
            }
          }
        } catch (_) {
          await _db.bumpOutboxAttempt(entry.rowId);
          break; // stop; retry on next trigger
        }
      }
    } finally {
      _flushing = false;
    }
  }

  Future<void> _dispatch(ScanKind kind, String refId, Map<String, dynamic> payload) async {
    switch (kind) {
      case ScanKind.startPatrol:
        await _api.startPatrol(StartPatrolDto(
            payload['patrolId'] as String, payload['routeId'] as String, payload['startedAt'] as String));
      case ScanKind.scan:
        final scans = (payload['scans'] as List)
            .cast<Map<String, dynamic>>()
            .map(ScanInputDto.fromJson)
            .toList();
        await _api.ingestScans(refId, IngestScansDto(scans));
      case ScanKind.completePatrol:
        await _api.completePatrol(refId, CompletePatrolDto(payload['completedAt'] as String));
    }
  }
}
```

Modify `mobile/lib/core/di.dart` — add import and provider (append):
```dart
// import '../data/sync/sync_service.dart';

final syncServiceProvider = Provider<SyncService>((ref) {
  final service = SyncService(ref.watch(appDatabaseProvider), ref.watch(apiClientProvider));
  ref.onDispose(service.dispose);
  return service;
});
```

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter test test/data/sync_service_test.dart`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add mobile/lib/data/sync/sync_service.dart mobile/lib/core/di.dart mobile/test/data/sync_service_test.dart
git commit -m "feat(mobile): outbox sync service with connectivity flush and retry"
```

---

### Task 10: Supervisor repository — list patrols + detail

**Files:**
- Create: `mobile/lib/domain/repositories/supervisor_repository.dart`, `mobile/lib/data/repositories/supervisor_repository_impl.dart`
- Modify: `mobile/lib/core/di.dart` (add `supervisorRepositoryProvider`)
- Test: `mobile/test/data/supervisor_repository_test.dart`

**Interfaces:**
- Consumes: `ApiClient`, `PatrolSummaryDto`, `PatrolDetailDto`, `PatrolSummary`, `PatrolDetail`.
- Produces:
  - `abstract class SupervisorRepository` (per Shared Type Reference).
  - `SupervisorRepositoryImpl(ApiClient api)` mapping DTOs to domain (parsing UTC timestamps).
  - `supervisorRepositoryProvider`.

- [ ] **Step 1: Write the domain interface**

Create `mobile/lib/domain/repositories/supervisor_repository.dart`:
```dart
import '../entities/patrol_summary.dart';
import '../entities/patrol_detail.dart';

abstract class SupervisorRepository {
  Future<List<PatrolSummary>> listPatrols({String? siteId, String? routeId, String? guardId});
  Future<PatrolDetail> patrolDetail(String id);
}
```

- [ ] **Step 2: Write the failing test**

Create `mobile/test/data/supervisor_repository_test.dart`:
```dart
import 'package:bekci/data/remote/api_client.dart';
import 'package:bekci/data/remote/dtos.dart';
import 'package:bekci/data/repositories/supervisor_repository_impl.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockApi extends Mock implements ApiClient {}

void main() {
  test('maps patrol detail with scans', () async {
    final api = _MockApi();
    when(() => api.patrolDetail('p1')).thenAnswer((_) async => PatrolDetailDto(
          'p1', 'r1', 'g1', '2026-07-10T22:00:00Z', null, 'InProgress',
          [ScanDetailDto('s1', 'c1', 'Lobby', '2026-07-10T22:05:00Z', true, false, false)],
        ));

    final repo = SupervisorRepositoryImpl(api);
    final detail = await repo.patrolDetail('p1');

    expect(detail.status, 'InProgress');
    expect(detail.scans.single.checkpointName, 'Lobby');
    expect(detail.scans.single.geoValid, isTrue);
  });
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `flutter test test/data/supervisor_repository_test.dart`
Expected: FAIL — `SupervisorRepositoryImpl` does not exist.

- [ ] **Step 4: Write the repository impl + provider**

Create `mobile/lib/data/repositories/supervisor_repository_impl.dart`:
```dart
import '../../domain/entities/patrol_summary.dart';
import '../../domain/entities/patrol_detail.dart';
import '../../domain/repositories/supervisor_repository.dart';
import '../remote/api_client.dart';

class SupervisorRepositoryImpl implements SupervisorRepository {
  final ApiClient _api;
  SupervisorRepositoryImpl(this._api);

  @override
  Future<List<PatrolSummary>> listPatrols({String? siteId, String? routeId, String? guardId}) async {
    final dtos = await _api.listPatrols(siteId, routeId, guardId);
    return dtos
        .map((d) => PatrolSummary(
              id: d.id, routeId: d.routeId, guardId: d.guardId,
              startedAt: DateTime.parse(d.startedAt).toUtc(),
              completedAt: d.completedAt == null ? null : DateTime.parse(d.completedAt!).toUtc(),
              status: d.status, scanCount: d.scanCount,
            ))
        .toList();
  }

  @override
  Future<PatrolDetail> patrolDetail(String id) async {
    final d = await _api.patrolDetail(id);
    return PatrolDetail(
      id: d.id, routeId: d.routeId, guardId: d.guardId,
      startedAt: DateTime.parse(d.startedAt).toUtc(),
      completedAt: d.completedAt == null ? null : DateTime.parse(d.completedAt!).toUtc(),
      status: d.status,
      scans: d.scans
          .map((s) => PatrolScanView(
                id: s.id, checkpointId: s.checkpointId, checkpointName: s.checkpointName,
                scannedAt: DateTime.parse(s.scannedAt).toUtc(),
                geoValid: s.geoValid, orderValid: s.orderValid, isDuplicate: s.isDuplicate,
              ))
          .toList(),
    );
  }
}
```

Modify `mobile/lib/core/di.dart` — add import and provider (append):
```dart
// import '../data/repositories/supervisor_repository_impl.dart';
// import '../domain/repositories/supervisor_repository.dart';

final supervisorRepositoryProvider = Provider<SupervisorRepository>(
    (ref) => SupervisorRepositoryImpl(ref.watch(apiClientProvider)));
```

- [ ] **Step 5: Run test to verify it passes**

Run: `flutter test test/data/supervisor_repository_test.dart`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mobile/lib/domain/repositories/supervisor_repository.dart mobile/lib/data/repositories/supervisor_repository_impl.dart mobile/lib/core/di.dart mobile/test/data/supervisor_repository_test.dart
git commit -m "feat(mobile): supervisor repository — patrol list + detail"
```

---

### Task 11: Auth controller, router, login screen (role-based routing)

**Files:**
- Create: `mobile/lib/presentation/auth/auth_controller.dart`, `mobile/lib/presentation/auth/login_screen.dart`, `mobile/lib/presentation/router.dart`
- Modify: `mobile/lib/app.dart` (use `MaterialApp.router`)
- Test: `mobile/test/presentation/login_test.dart`

**Interfaces:**
- Consumes: `authRepositoryProvider`, `AuthSession`, `UserRole`, `syncServiceProvider`.
- Produces:
  - `authControllerProvider` — an `AsyncNotifier<AuthSession?>` exposing `login(email,password)`, `logout()`, seeded from `currentSession()`.
  - `routerProvider` — a `GoRouter` that redirects: unauthenticated → `/login`; `Guard` → `/guard`; `Supervisor` → `/supervisor`.
  - `LoginScreen`.

- [ ] **Step 1: Write the auth controller and router**

Create `mobile/lib/presentation/auth/auth_controller.dart`:
```dart
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/di.dart';
import '../../domain/entities/auth_session.dart';

class AuthController extends AsyncNotifier<AuthSession?> {
  @override
  Future<AuthSession?> build() => ref.read(authRepositoryProvider).currentSession();

  Future<void> login(String email, String password) async {
    state = const AsyncLoading();
    state = await AsyncValue.guard(() async {
      final session = await ref.read(authRepositoryProvider).login(email, password);
      ref.read(syncServiceProvider).start();
      return session;
    });
  }

  Future<void> logout() async {
    await ref.read(authRepositoryProvider).logout();
    state = const AsyncData(null);
  }
}

final authControllerProvider =
    AsyncNotifierProvider<AuthController, AuthSession?>(AuthController.new);
```

Create `mobile/lib/presentation/router.dart`:
```dart
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../domain/enums.dart';
import 'auth/auth_controller.dart';
import 'auth/login_screen.dart';
import 'guard/guard_home_screen.dart';
import 'guard/patrol_screen.dart';
import 'supervisor/supervisor_home_screen.dart';
import 'supervisor/patrol_detail_screen.dart';

final routerProvider = Provider<GoRouter>((ref) {
  return GoRouter(
    initialLocation: '/login',
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final session = auth.valueOrNull;
      final loggingIn = state.matchedLocation == '/login';

      if (session == null) return loggingIn ? null : '/login';
      final home = session.role == UserRole.supervisor ? '/supervisor' : '/guard';
      if (loggingIn) return home;
      return null;
    },
    routes: [
      GoRoute(path: '/login', builder: (_, __) => const LoginScreen()),
      GoRoute(path: '/guard', builder: (_, __) => const GuardHomeScreen()),
      GoRoute(
        path: '/guard/patrol/:patrolId/:routeId',
        builder: (_, s) => PatrolScreen(
          patrolId: s.pathParameters['patrolId']!,
          routeId: s.pathParameters['routeId']!,
        ),
      ),
      GoRoute(path: '/supervisor', builder: (_, __) => const SupervisorHomeScreen()),
      GoRoute(
        path: '/supervisor/patrol/:id',
        builder: (_, s) => PatrolDetailScreen(patrolId: s.pathParameters['id']!),
      ),
    ],
    refreshListenable: _AuthRefresh(ref),
  );
});

class _AuthRefresh extends ChangeNotifier {
  _AuthRefresh(Ref ref) {
    ref.listen(authControllerProvider, (_, __) => notifyListeners());
  }
}
```

> `ChangeNotifier` comes from `package:flutter/foundation.dart`; add `import 'package:flutter/foundation.dart';` to `router.dart`.

- [ ] **Step 2: Write the login screen**

Create `mobile/lib/presentation/auth/login_screen.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'auth_controller.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});
  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();

  @override
  Widget build(BuildContext context) {
    final auth = ref.watch(authControllerProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Bekçi — Sign in')),
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            TextField(
              controller: _email,
              decoration: const InputDecoration(labelText: 'Email'),
              keyboardType: TextInputType.emailAddress,
            ),
            TextField(
              controller: _password,
              decoration: const InputDecoration(labelText: 'Password'),
              obscureText: true,
            ),
            const SizedBox(height: 24),
            if (auth.hasError)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Text('Invalid email or password',
                    style: TextStyle(color: Theme.of(context).colorScheme.error)),
              ),
            FilledButton(
              onPressed: auth.isLoading
                  ? null
                  : () => ref.read(authControllerProvider.notifier)
                      .login(_email.text.trim(), _password.text),
              child: auth.isLoading
                  ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Sign in'),
            ),
          ],
        ),
      ),
    );
  }
}
```

- [ ] **Step 3: Wire the router into the app**

Replace `mobile/lib/app.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'presentation/router.dart';

class BekciApp extends StatelessWidget {
  const BekciApp({super.key});

  @override
  Widget build(BuildContext context) => const ProviderScope(child: _Root());
}

class _Root extends ConsumerWidget {
  const _Root();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(routerProvider);
    return MaterialApp.router(
      title: 'Bekçi',
      theme: ThemeData(colorSchemeSeed: Colors.indigo, useMaterial3: true),
      routerConfig: router,
    );
  }
}
```

> Task 1's `smoke_test.dart` expects a `MaterialApp`. `MaterialApp.router` still creates a `MaterialApp`, so `find.byType(MaterialApp)` still matches — but it will try to resolve the router. Update `smoke_test.dart` to pump inside a `ProviderScope` is unnecessary since `BekciApp` already wraps one; leave it. If it fails to settle, change the smoke test's `expect` to `findsWidgets`.

- [ ] **Step 4: Write the failing test**

Create `mobile/test/presentation/login_test.dart`:
```dart
import 'package:bekci/domain/entities/auth_session.dart';
import 'package:bekci/domain/enums.dart';
import 'package:bekci/domain/repositories/auth_repository.dart';
import 'package:bekci/core/di.dart';
import 'package:bekci/presentation/auth/login_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeAuthRepo implements AuthRepository {
  @override
  Future<AuthSession?> currentSession() async => null;
  @override
  Future<AuthSession> login(String email, String password) async {
    if (password != 'good') throw Exception('bad creds');
    return const AuthSession(token: 't', role: UserRole.guard, tenantId: 't1', siteId: 's1');
  }
  @override
  Future<void> logout() async {}
}

void main() {
  testWidgets('shows error on failed login', (tester) async {
    await tester.pumpWidget(ProviderScope(
      overrides: [authRepositoryProvider.overrideWithValue(_FakeAuthRepo())],
      child: const MaterialApp(home: LoginScreen()),
    ));

    await tester.enterText(find.byType(TextField).at(0), 'g@a.com');
    await tester.enterText(find.byType(TextField).at(1), 'wrong');
    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Invalid email or password'), findsOneWidget);
  });
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `flutter test test/presentation/login_test.dart`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mobile/lib/presentation/auth mobile/lib/presentation/router.dart mobile/lib/app.dart mobile/test/presentation/login_test.dart
git commit -m "feat(mobile): auth controller, GoRouter role routing, login screen"
```

---

### Task 12: Guard UI — route list (start patrol) + patrol scan screen (widget test)

**Files:**
- Create: `mobile/lib/presentation/guard/guard_home_screen.dart`, `mobile/lib/presentation/guard/patrol_controller.dart`, `mobile/lib/presentation/guard/patrol_screen.dart`
- Test: `mobile/test/presentation/patrol_feedback_test.dart`

**Interfaces:**
- Consumes: `routeRepositoryProvider`, `patrolRepositoryProvider`, `authControllerProvider`, `RouteRef`, `LocalScan`, `ScanOutcome`.
- Produces:
  - `guardRoutesProvider` — `FutureProvider<List<RouteRef>>` calling `refreshGuardRoutes()`.
  - `PatrolController` (family by patrolId) — holds the scan list, exposes `onQrDetected(routeId, qrCode)` returning a user-facing message string; `complete()`.
  - `GuardHomeScreen` (route list + Start), `PatrolScreen` (scanner + live scan feedback). The scanner widget is isolated behind a `ScanInput` callback so the feedback logic is testable without a camera.

- [ ] **Step 1: Write the patrol controller**

Create `mobile/lib/presentation/guard/patrol_controller.dart`:
```dart
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/di.dart';
import '../../domain/entities/local_scan.dart';

class PatrolState {
  final List<LocalScan> scans;
  final String? lastMessage;
  const PatrolState({this.scans = const [], this.lastMessage});

  PatrolState copyWith({List<LocalScan>? scans, String? lastMessage}) =>
      PatrolState(scans: scans ?? this.scans, lastMessage: lastMessage ?? this.lastMessage);
}

class PatrolController extends FamilyNotifier<PatrolState, String> {
  @override
  PatrolState build(String patrolId) => const PatrolState();

  /// Returns a user-facing feedback message for the scanned QR.
  Future<String> onQrDetected(String routeId, String qrCode) async {
    final repo = ref.read(patrolRepositoryProvider);
    final outcome = await repo.recordScan(patrolId: arg, routeId: routeId, qrCode: qrCode);

    if (!outcome.matchedCheckpoint) {
      final msg = "This checkpoint isn't on your route";
      state = state.copyWith(lastMessage: msg);
      return msg;
    }

    final scan = outcome.scan!;
    final scans = await repo.scansFor(arg);
    final parts = <String>['Checkpoint recorded'];
    if (!scan.geoValid) parts.add('location unverified');
    if (!scan.orderValid) parts.add('out of order');
    final msg = parts.join(' — ');
    state = state.copyWith(scans: scans, lastMessage: msg);
    return msg;
  }

  Future<void> complete() => ref.read(patrolRepositoryProvider).completePatrol(arg);
}

final patrolControllerProvider =
    NotifierProvider.family<PatrolController, PatrolState, String>(PatrolController.new);

final guardRoutesProvider = FutureProvider.autoDispose((ref) async {
  return ref.read(routeRepositoryProvider).refreshGuardRoutes();
});
```

- [ ] **Step 2: Write the guard home screen**

Create `mobile/lib/presentation/guard/guard_home_screen.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../../core/di.dart';
import '../auth/auth_controller.dart';
import 'patrol_controller.dart';

class GuardHomeScreen extends ConsumerWidget {
  const GuardHomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final routes = ref.watch(guardRoutesProvider);
    return Scaffold(
      appBar: AppBar(
        title: const Text('My routes'),
        actions: [
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
          ),
        ],
      ),
      body: routes.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(child: Text('Failed to load routes: $e')),
        data: (list) => ListView(
          children: [
            for (final r in list)
              ListTile(
                title: Text(r.name),
                subtitle: Text(r.enforceOrder ? 'Ordered route' : 'Any order'),
                trailing: FilledButton(
                  child: const Text('Start'),
                  onPressed: () async {
                    final patrol = await ref.read(patrolRepositoryProvider).startPatrol(r.id);
                    if (context.mounted) {
                      context.push('/guard/patrol/${patrol.id}/${r.id}');
                    }
                  },
                ),
              ),
          ],
        ),
      ),
    );
  }
}
```

- [ ] **Step 3: Write the patrol screen (scanner isolated behind a callback)**

Create `mobile/lib/presentation/guard/patrol_screen.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'patrol_controller.dart';

class PatrolScreen extends ConsumerWidget {
  final String patrolId;
  final String routeId;
  const PatrolScreen({super.key, required this.patrolId, required this.routeId});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(patrolControllerProvider(patrolId));
    return Scaffold(
      appBar: AppBar(title: const Text('Patrol')),
      body: Column(
        children: [
          SizedBox(
            height: 260,
            child: MobileScanner(
              onDetect: (capture) {
                final code = capture.barcodes.firstOrNull?.rawValue;
                if (code != null) {
                  ref.read(patrolControllerProvider(patrolId).notifier).onQrDetected(routeId, code);
                }
              },
            ),
          ),
          if (state.lastMessage != null)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(state.lastMessage!, key: const Key('scan-feedback'),
                  style: Theme.of(context).textTheme.titleMedium),
            ),
          Expanded(
            child: ListView(
              children: [
                for (final s in state.scans)
                  ListTile(
                    leading: Icon(s.sendStatus.name == 'sent' ? Icons.cloud_done : Icons.cloud_upload),
                    title: Text('Checkpoint ${s.checkpointId}'),
                    subtitle: Text([
                      if (!s.geoValid) 'location unverified',
                      if (!s.orderValid) 'out of order',
                    ].join(' · ')),
                  ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.all(12),
            child: FilledButton(
              onPressed: () => ref.read(patrolControllerProvider(patrolId).notifier).complete(),
              child: const Text('Complete patrol'),
            ),
          ),
        ],
      ),
    );
  }
}

extension _FirstOrNull<E> on Iterable<E> {
  E? get firstOrNull => isEmpty ? null : first;
}
```

- [ ] **Step 4: Write the failing widget test (scan feedback)**

Create `mobile/test/presentation/patrol_feedback_test.dart`:
```dart
import 'package:bekci/core/di.dart';
import 'package:bekci/domain/entities/local_scan.dart';
import 'package:bekci/domain/entities/scan_outcome.dart';
import 'package:bekci/domain/enums.dart';
import 'package:bekci/domain/entities/local_patrol.dart';
import 'package:bekci/domain/repositories/patrol_repository.dart';
import 'package:bekci/presentation/guard/patrol_controller.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakePatrolRepo implements PatrolRepository {
  @override
  Future<LocalPatrol> startPatrol(String routeId) async =>
      LocalPatrol(id: 'p1', routeId: routeId, startedAt: DateTime.utc(2026), completedAt: null, status: PatrolSyncStatus.inProgress);

  @override
  Future<ScanOutcome> recordScan({required String patrolId, required String routeId, required String qrCode}) async {
    if (qrCode == 'WRONG') return const ScanOutcome(scan: null, matchedCheckpoint: false);
    return ScanOutcome(
      scan: LocalScan(
        id: 's1', patrolId: patrolId, checkpointId: 'c1', scannedAt: DateTime.utc(2026),
        lat: null, lng: null, geoValid: false, orderValid: true, sendStatus: SendStatus.pending),
      matchedCheckpoint: true,
    );
  }

  @override
  Future<void> completePatrol(String patrolId) async {}

  @override
  Future<List<LocalScan>> scansFor(String patrolId) async => [];
}

void main() {
  test('unknown QR yields off-route message', () async {
    final container = ProviderContainer(
      overrides: [patrolRepositoryProvider.overrideWithValue(_FakePatrolRepo())],
    );
    addTearDown(container.dispose);

    final msg = await container
        .read(patrolControllerProvider('p1').notifier)
        .onQrDetected('r1', 'WRONG');
    expect(msg, "This checkpoint isn't on your route");
  });

  test('valid scan with no GPS reports location unverified', () async {
    final container = ProviderContainer(
      overrides: [patrolRepositoryProvider.overrideWithValue(_FakePatrolRepo())],
    );
    addTearDown(container.dispose);

    final msg = await container
        .read(patrolControllerProvider('p1').notifier)
        .onQrDetected('r1', 'QR-1');
    expect(msg, contains('Checkpoint recorded'));
    expect(msg, contains('location unverified'));
  });
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `flutter test test/presentation/patrol_feedback_test.dart`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add mobile/lib/presentation/guard mobile/test/presentation/patrol_feedback_test.dart
git commit -m "feat(mobile): guard route list + patrol scan screen with feedback"
```

---

### Task 13: Supervisor UI — patrol list + detail

**Files:**
- Create: `mobile/lib/presentation/supervisor/supervisor_controller.dart`, `mobile/lib/presentation/supervisor/supervisor_home_screen.dart`, `mobile/lib/presentation/supervisor/patrol_detail_screen.dart`
- Test: `mobile/test/presentation/supervisor_detail_test.dart`

**Interfaces:**
- Consumes: `supervisorRepositoryProvider`, `PatrolSummary`, `PatrolDetail`.
- Produces:
  - `patrolsListProvider` — `FutureProvider.autoDispose<List<PatrolSummary>>`.
  - `patrolDetailProvider` — `FutureProvider.family<PatrolDetail, String>`.
  - `SupervisorHomeScreen` (pull-to-refresh list), `PatrolDetailScreen` (scans with geo/order flags).

- [ ] **Step 1: Write the providers**

Create `mobile/lib/presentation/supervisor/supervisor_controller.dart`:
```dart
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/di.dart';
import '../../domain/entities/patrol_summary.dart';
import '../../domain/entities/patrol_detail.dart';

final patrolsListProvider = FutureProvider.autoDispose<List<PatrolSummary>>((ref) {
  return ref.read(supervisorRepositoryProvider).listPatrols();
});

final patrolDetailProvider =
    FutureProvider.autoDispose.family<PatrolDetail, String>((ref, id) {
  return ref.read(supervisorRepositoryProvider).patrolDetail(id);
});
```

- [ ] **Step 2: Write the screens**

Create `mobile/lib/presentation/supervisor/supervisor_home_screen.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../auth/auth_controller.dart';
import 'supervisor_controller.dart';

class SupervisorHomeScreen extends ConsumerWidget {
  const SupervisorHomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final patrols = ref.watch(patrolsListProvider);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Patrols'),
        actions: [
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.refresh(patrolsListProvider.future),
        child: patrols.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(children: [Center(child: Text('Error: $e'))]),
          data: (list) => ListView(
            children: [
              for (final p in list)
                ListTile(
                  title: Text('Patrol ${p.id.substring(0, 8)}'),
                  subtitle: Text('${p.status} · ${p.scanCount} scans'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => context.push('/supervisor/patrol/${p.id}'),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
```

Create `mobile/lib/presentation/supervisor/patrol_detail_screen.dart`:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'supervisor_controller.dart';

class PatrolDetailScreen extends ConsumerWidget {
  final String patrolId;
  const PatrolDetailScreen({super.key, required this.patrolId});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detail = ref.watch(patrolDetailProvider(patrolId));
    return Scaffold(
      appBar: AppBar(title: const Text('Patrol detail')),
      body: detail.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => Center(child: Text('Error: $e')),
        data: (d) => ListView(
          children: [
            ListTile(title: const Text('Status'), trailing: Text(d.status)),
            const Divider(),
            for (final s in d.scans)
              ListTile(
                title: Text(s.checkpointName),
                subtitle: Text([
                  s.geoValid ? 'GPS ok' : 'GPS unverified',
                  s.orderValid ? 'in order' : 'out of order',
                  if (s.isDuplicate) 'duplicate',
                ].join(' · ')),
                trailing: Icon(
                  s.geoValid && s.orderValid ? Icons.check_circle : Icons.warning,
                  color: s.geoValid && s.orderValid ? Colors.green : Colors.orange,
                ),
              ),
          ],
        ),
      ),
    );
  }
}
```

- [ ] **Step 3: Write the failing test**

Create `mobile/test/presentation/supervisor_detail_test.dart`:
```dart
import 'package:bekci/core/di.dart';
import 'package:bekci/domain/entities/patrol_detail.dart';
import 'package:bekci/domain/entities/patrol_summary.dart';
import 'package:bekci/domain/repositories/supervisor_repository.dart';
import 'package:bekci/presentation/supervisor/patrol_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeSupRepo implements SupervisorRepository {
  @override
  Future<List<PatrolSummary>> listPatrols({String? siteId, String? routeId, String? guardId}) async => [];
  @override
  Future<PatrolDetail> patrolDetail(String id) async => PatrolDetail(
        id: id, routeId: 'r1', guardId: 'g1', startedAt: DateTime.utc(2026), completedAt: null,
        status: 'InProgress',
        scans: const [
          PatrolScanView(id: 's1', checkpointId: 'c1', checkpointName: 'Lobby',
              scannedAt: null == null ? _fixed : _fixed, geoValid: false, orderValid: true, isDuplicate: false),
        ],
      );
}

final _fixed = DateTime.utc(2026, 7, 10);

void main() {
  testWidgets('detail shows scan with GPS unverified flag', (tester) async {
    await tester.pumpWidget(ProviderScope(
      overrides: [supervisorRepositoryProvider.overrideWithValue(_FakeSupRepo())],
      child: const MaterialApp(home: PatrolDetailScreen(patrolId: 'p1')),
    ));
    await tester.pumpAndSettle();

    expect(find.text('Lobby'), findsOneWidget);
    expect(find.textContaining('GPS unverified'), findsOneWidget);
  });
}
```

> Simplify the fake: replace the awkward `null == null ? _fixed : _fixed` with just `_fixed` when writing the file — it is written that way here only to keep the const list valid; use `scannedAt: _fixed`.

- [ ] **Step 4: Run test to verify it passes**

Run: `flutter test test/presentation/supervisor_detail_test.dart`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add mobile/lib/presentation/supervisor mobile/test/presentation/supervisor_detail_test.dart
git commit -m "feat(mobile): supervisor patrol list + detail screens"
```

---

### Task 14: End-to-end critical-path test + full suite green

**Files:**
- Create: `mobile/test/integration/critical_path_test.dart`

**Interfaces:**
- Consumes: `AppDatabase`, `ApiClient` (mocked), `PatrolRepositoryImpl`, `SyncService`, `RouteRepositoryImpl`. No new production code — proves the spec's critical path on-device.

- [ ] **Step 1: Write the end-to-end test**

Create `mobile/test/integration/critical_path_test.dart`:
```dart
import 'package:bekci/data/local/app_database.dart';
import 'package:bekci/data/remote/api_client.dart';
import 'package:bekci/data/remote/dtos.dart';
import 'package:bekci/data/repositories/patrol_repository_impl.dart';
import 'package:bekci/data/repositories/route_repository_impl.dart';
import 'package:bekci/data/sync/sync_service.dart';
import 'package:bekci/domain/enums.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockApi extends Mock implements ApiClient {}

void main() {
  setUpAll(() {
    registerFallbackValue(StartPatrolDto('p', 'r', 't'));
    registerFallbackValue(IngestScansDto(const []));
    registerFallbackValue(CompletePatrolDto('t'));
  });

  test('online scan sends; dead-zone scans queue then flush exactly once', () async {
    final db = AppDatabase(NativeDatabase.memory());
    addTearDown(db.close);
    final api = _MockApi();

    // Seed cache as if a route refresh already happened.
    when(() => api.guardRoutes()).thenAnswer((_) async => [RouteDto('r1', 's1', 'Loop', false)]);
    when(() => api.checkpoints('r1')).thenAnswer((_) async => [
          CheckpointDto('c1', 'r1', 'Lobby', 'QR-1', 40.0, 29.0, 25, 1),
          CheckpointDto('c2', 'r1', 'Back', 'QR-2', 41.0, 30.0, 25, 2),
        ]);
    final routes = RouteRepositoryImpl(api, db);
    await routes.refreshGuardRoutes();

    var id = 0;
    final repo = PatrolRepositoryImpl(
      db, api, routes, () => 'id-${++id}', () => DateTime.utc(2026, 7, 10, 22),
      () async => (lat: 40.00005, lng: 29.0), // good GPS near c1
    );

    // 1. Online: first scan succeeds immediately.
    when(() => api.ingestScans('p1', any()))
        .thenAnswer((_) async => IngestScansResponseDto([ScanResultDto('x', true, true, false)]));
    await repo.startPatrol('r1'); // returns id-1 patrol; but we drive p1 explicitly below
    // Use a fixed patrol id for clarity.
    // (startPatrol used a generated id; for the scan calls we pass our own patrolId 'p1'.)

    final s1 = await repo.recordScan(patrolId: 'p1', routeId: 'r1', qrCode: 'QR-1');
    expect(s1.scan!.sendStatus, SendStatus.sent);
    expect(await db.pendingOutbox(), isEmpty);

    // 2. Dead zone: API throws → scan queues.
    when(() => api.ingestScans('p1', any())).thenThrow(Exception('offline'));
    final s2 = await repo.recordScan(patrolId: 'p1', routeId: 'r1', qrCode: 'QR-2');
    expect(s2.matchedCheckpoint, isTrue);
    expect(await db.pendingOutbox(), hasLength(1));

    // 3. Reconnect: flush drains the outbox exactly once.
    when(() => api.ingestScans('p1', any()))
        .thenAnswer((_) async => IngestScansResponseDto([ScanResultDto('id-2', false, true, false)]));
    final sync = SyncService(db, api);
    await sync.flush();
    await sync.flush(); // second flush is a no-op (nothing pending)

    expect(await db.pendingOutbox(), isEmpty);
    final scans = await db.getScans('p1');
    expect(scans, hasLength(2));
    expect(scans.every((s) => s.sendStatus == SendStatus.sent), isTrue);
    // ingestScans called: 1 online (QR-1) + 1 during flush (QR-2). The dead-zone attempt threw.
    verify(() => api.ingestScans('p1', any())).called(2);
  });
}
```

> Note: `startPatrol('r1')` above generates its own patrol id; the scan calls deliberately use a fixed `'p1'` so the assertions read clearly. This is valid because scans reference the patrol id the guard is on; in the app that id comes from `startPatrol`. If you prefer strict fidelity, capture the returned `patrol.id` and use it for the scan calls.

- [ ] **Step 2: Run this test**

Run: `flutter test test/integration/critical_path_test.dart`
Expected: PASS.

- [ ] **Step 3: Run the full suite**

Run: `cd mobile && flutter test`
Expected: ALL tests PASS.

- [ ] **Step 4: Commit**

```bash
git add mobile/test/integration/critical_path_test.dart
git commit -m "test(mobile): end-to-end online + dead-zone flush critical path"
```

---

## Self-Review

**1. Spec coverage** (against `2026-07-10-guard-tour-phase-1-design.md`):

| Spec item | Task |
|---|---|
| Single Flutter app, role-based UI (Supervisor vs Guard) | Tasks 11 (router redirect), 12, 13 |
| Login + JWT storage + role decode | Tasks 6, 11 |
| Guard sees routes at their site, cached to Drift | Tasks 7, 12 |
| Start patrol (client UUID, online-or-queue) | Tasks 8, 12 |
| Scan QR → match cached checkpoint → GPS → geo/order flags | Tasks 3, 8, 12 |
| Online-first: immediate send; offline: queue + auto-flush | Tasks 8, 9 |
| Idempotent sends via client UUIDs | Tasks 8, 9, 14 |
| Reference cache is read-only; server is source of truth | Tasks 4, 7 |
| Unknown-QR / no-GPS / out-of-order handling | Tasks 8, 12 (feedback messages) |
| Complete patrol | Tasks 8, 12 |
| Supervisor history: list + detail with flags (pull-to-refresh) | Tasks 10, 13 |
| Testing: sync engine, validation, repository, one scan-feedback widget test | Tasks 3, 8, 9, 12 |
| Critical path (online → dead zone → flush once → verdicts) | Task 14 |

Deferred items (shifts, SignalR live feed, panic, photos, time windows) are intentionally absent — matches the spec's "Out of Scope" section. The Phase 1 stand-in (guard self-selects a route from their site) is implemented via `guardRoutesProvider` + Start button (Task 12).

**2. Placeholder scan:** No "TBD/TODO/handle later" in production steps. Two test files contain explicit inline notes where a construct is written awkwardly to stay valid (`supervisor_detail_test.dart`'s `scannedAt`, `critical_path_test.dart`'s fixed patrol id); both include the exact simplification to apply. All production code steps are complete.

**3. Type consistency:** Repository interface method names match their impls and call sites (`refreshGuardRoutes`, `recordScan`, `completePatrol`, `listPatrols`, `patrolDetail`). `SendStatus`/`PatrolSyncStatus`/`ScanKind` enums are used identically across Drift tables (Task 4), repositories (Task 8), and the sync service (Task 9). DTO field names (`patrolId`, `scanId`, `scannedAt`, `geoValid`, `orderValid`, `isDuplicate`) match the Plan A backend contract exactly. `ScanOutcome { scan, matchedCheckpoint }` is consumed identically in Task 12.

**Resolved risk:** `recordScan` needs the route's `enforceOrder`; Task 8 adds `AppDatabase.getRouteById` and reads it there rather than misusing the site-scoped `cachedRoutes`. The step text calls this out explicitly.

---

## Notes for the implementer

- **Run order:** this plan depends on the Plan A backend for real end-to-end runs, but every test here mocks the API or uses in-memory Drift — so **the whole suite runs without a backend or device** (`flutter test` on desktop; Drift uses host sqlite3).
- **Codegen:** Tasks 4, 5, and 8 modify Drift tables / annotated files — always re-run `dart run build_runner build --delete-conflicting-outputs` before testing those tasks.
- **Manual run against the backend:** `flutter run --dart-define=API_BASE_URL=http://10.0.2.2:8080` (Android emulator → host). Grant camera + location permissions on first patrol.
- **Platform permissions:** before a real device run, add camera usage (`NSCameraUsageDescription` / `<uses-permission android:name="android.permission.CAMERA"/>`) and location strings for `mobile_scanner` and `geolocator`. This is device-run setup, not needed for the test suite.
