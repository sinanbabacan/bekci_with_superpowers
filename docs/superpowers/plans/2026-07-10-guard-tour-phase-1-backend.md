# Bekçi Guard Tour — Phase 1 Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the multi-tenant .NET backend for the Phase 1 guard-tour vertical patrol loop: auth, supervisor route/checkpoint configuration, guard patrol + scan ingestion, and supervisor history — all tenant-isolated and covered by tests.

**Architecture:** Clean Architecture (`Domain` / `Application` / `Infrastructure` / `Api`) mirroring the `sparkion-loyalty` repo. Domain entities use a `Guid`-keyed `Entity` base with `Create` factories. Multi-tenancy is enforced by an EF Core global query filter driven by an `ITenantContext` resolved from a JWT `tenant_id` claim. The device is the source of truth for scan timestamps; the scan ingestion endpoint is idempotent (client-generated GUIDs) and re-validates geo/order on the server.

**Tech Stack:** .NET 10, ASP.NET Core Web API, EF Core 10 + Npgsql/PostgreSQL, JWT Bearer auth, BCrypt.Net-Next, FluentValidation, xUnit + Testcontainers.PostgreSql + Microsoft.AspNetCore.Mvc.Testing + FluentAssertions.

## Global Constraints

- **Target framework:** `net10.0` for every project (copy from existing repos).
- **Project prefix / namespaces:** `Bekci.Domain`, `Bekci.Application`, `Bekci.Infrastructure`, `Bekci.Api`, tests in `Bekci.Tests`. Solution file `Bekci.sln` at repo root.
- **Layout:** source under `src/`, tests under `tests/`.
- **IDs:** every entity derives from `Bekci.Domain.Entity` (a `Guid Id`). `Patrol` and `Scan` IDs are **client-supplied** GUIDs; all other IDs are server-generated with `Guid.NewGuid()`.
- **Tenant scoping:** every entity except `Organization` implements `ITenantEntity` (`Guid TenantId`) and is filtered by a global query filter. Never write a query that bypasses the filter except the login lookup and design-time seeding, which use `.IgnoreQueryFilters()`.
- **Times:** all timestamps are UTC `DateTime` (`DateTime.UtcNow`), stored as Postgres `timestamptz`.
- **Enums stored as strings:** `UserRole` and `PatrolStatus` persist via `.HasConversion<string>()`.
- **Password hashing:** `BCrypt.Net.BCrypt.HashPassword` / `.Verify` (package `BCrypt.Net-Next`).
- **Routing:** controllers use `[Route("api/v1/<resource>")]`. All endpoints require auth except `POST /api/v1/auth/login` (`[AllowAnonymous]`).
- **Geofence is soft:** a scan is never rejected for being outside the geofence or out of order; those conditions set boolean flags only.
- **Commit after every task** with the message shown in that task's final step.

---

## File Structure

```
Bekci.sln
src/
  Bekci.Domain/
    Bekci.Domain.csproj
    Entity.cs                 # abstract base (Guid Id)
    ITenantEntity.cs          # Guid TenantId
    Enums.cs                  # UserRole, PatrolStatus
    Entities/
      Organization.cs
      User.cs
      Site.cs
      Route.cs
      Checkpoint.cs
      Patrol.cs
      Scan.cs
  Bekci.Application/
    Bekci.Application.csproj
    Abstractions/ITenantContext.cs
    Data/Repository.cs        # DbContext
    Data/Configurations/*.cs  # EF entity configs
    DTOs/*.cs
    Services/*.cs             # SiteService, RouteService, CheckpointService, PatrolService, ScanService, PatrolQueryService
    Validators/*.cs
    DependencyInjection.cs    # AddApplication()
  Bekci.Infrastructure/
    Bekci.Infrastructure.csproj
    DependencyInjection.cs    # AddInfrastructure() — DbContext registration
    Migrations/
  Bekci.Api/
    Bekci.Api.csproj
    Program.cs
    appsettings.json
    Auth/AuthService.cs
    Auth/TenantContext.cs     # ITenantContext from HttpContext claims
    Controllers/*.cs
tests/
  Bekci.Tests/
    Bekci.Tests.csproj
    Domain/*.cs               # pure unit tests
    Integration/*.cs          # Testcontainers + WebApplicationFactory
    Integration/ApiFactory.cs
```

---

## Shared Type Reference (defined across tasks — listed here for consistency)

- `abstract class Entity { public Guid Id { get; protected set; } }`
- `interface ITenantEntity { Guid TenantId { get; } }`
- `enum UserRole { Supervisor, Guard }`
- `enum PatrolStatus { InProgress, Completed, Abandoned }`
- `interface ITenantContext { Guid TenantId { get; } bool HasTenant { get; } }`
- `class Repository : DbContext` with `DbSet`s: `Organizations, Users, Sites, Routes, Checkpoints, Patrols, Scans`.
- Scan verdict helper: `static class ScanValidation { static bool IsWithinGeofence(double cpLat, double cpLng, double radiusM, double? lat, double? lng); }`

---

### Task 1: Solution scaffold + Domain base + Organization entity

**Files:**
- Create: `Bekci.sln`, `src/Bekci.Domain/Bekci.Domain.csproj`, `src/Bekci.Application/Bekci.Application.csproj`, `src/Bekci.Infrastructure/Bekci.Infrastructure.csproj`, `src/Bekci.Api/Bekci.Api.csproj`, `tests/Bekci.Tests/Bekci.Tests.csproj`
- Create: `src/Bekci.Domain/Entity.cs`, `src/Bekci.Domain/ITenantEntity.cs`, `src/Bekci.Domain/Enums.cs`, `src/Bekci.Domain/Entities/Organization.cs`
- Test: `tests/Bekci.Tests/Domain/OrganizationTests.cs`

**Interfaces:**
- Produces: `Entity`, `ITenantEntity`, `UserRole`, `PatrolStatus`, `Organization.Create(string name)`.

- [ ] **Step 1: Create the solution and projects**

Run:
```bash
cd /Users/sinanbabacan/repos/bekci_with_superpowers
dotnet new sln -n Bekci
dotnet new classlib -n Bekci.Domain -o src/Bekci.Domain -f net10.0
dotnet new classlib -n Bekci.Application -o src/Bekci.Application -f net10.0
dotnet new classlib -n Bekci.Infrastructure -o src/Bekci.Infrastructure -f net10.0
dotnet new webapi -n Bekci.Api -o src/Bekci.Api -f net10.0 --use-controllers
dotnet new xunit -n Bekci.Tests -o tests/Bekci.Tests -f net10.0
dotnet sln add src/Bekci.Domain src/Bekci.Application src/Bekci.Infrastructure src/Bekci.Api tests/Bekci.Tests
rm -f src/Bekci.Domain/Class1.cs src/Bekci.Application/Class1.cs src/Bekci.Infrastructure/Class1.cs
dotnet add src/Bekci.Application reference src/Bekci.Domain
dotnet add src/Bekci.Infrastructure reference src/Bekci.Application
dotnet add src/Bekci.Api reference src/Bekci.Application src/Bekci.Infrastructure
dotnet add tests/Bekci.Tests reference src/Bekci.Domain src/Bekci.Application src/Bekci.Api
dotnet add tests/Bekci.Tests package FluentAssertions
```
Expected: all commands succeed, `dotnet build` (next step) can find projects.

- [ ] **Step 2: Write the failing test**

Create `tests/Bekci.Tests/Domain/OrganizationTests.cs`:
```csharp
using Bekci.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class OrganizationTests
{
    [Fact]
    public void Create_sets_name_and_generates_id()
    {
        var org = Organization.Create("Acme Security");

        org.Name.Should().Be("Acme Security");
        org.Id.Should().NotBe(Guid.Empty);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter OrganizationTests`
Expected: FAIL — `Organization` does not exist (compile error).

- [ ] **Step 4: Write the domain base and Organization**

Create `src/Bekci.Domain/Entity.cs`:
```csharp
namespace Bekci.Domain;

public abstract class Entity
{
    public Guid Id { get; protected set; }
}
```

Create `src/Bekci.Domain/ITenantEntity.cs`:
```csharp
namespace Bekci.Domain;

public interface ITenantEntity
{
    Guid TenantId { get; }
}
```

Create `src/Bekci.Domain/Enums.cs`:
```csharp
namespace Bekci.Domain;

public enum UserRole
{
    Supervisor,
    Guard
}

public enum PatrolStatus
{
    InProgress,
    Completed,
    Abandoned
}
```

Create `src/Bekci.Domain/Entities/Organization.cs`:
```csharp
namespace Bekci.Domain.Entities;

public sealed class Organization : Entity
{
    public string Name { get; private set; } = default!;

    private Organization() { }

    public static Organization Create(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name
    };
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter OrganizationTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Bekci.sln src tests
git commit -m "feat: scaffold Clean Architecture solution + Organization domain"
```

---

### Task 2: User entity

**Files:**
- Create: `src/Bekci.Domain/Entities/User.cs`
- Test: `tests/Bekci.Tests/Domain/UserTests.cs`

**Interfaces:**
- Consumes: `Entity`, `ITenantEntity`, `UserRole`.
- Produces: `User.Create(Guid tenantId, string email, string passwordHash, UserRole role, Guid? siteId)`; properties `TenantId, Email, PasswordHash, Role, SiteId`.

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Domain/UserTests.cs`:
```csharp
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class UserTests
{
    [Fact]
    public void Create_guard_sets_all_fields()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var user = User.Create(tenantId, "guard@acme.com", "hash", UserRole.Guard, siteId);

        user.TenantId.Should().Be(tenantId);
        user.Email.Should().Be("guard@acme.com");
        user.PasswordHash.Should().Be("hash");
        user.Role.Should().Be(UserRole.Guard);
        user.SiteId.Should().Be(siteId);
        user.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_supervisor_has_no_site()
    {
        var user = User.Create(Guid.NewGuid(), "sup@acme.com", "hash", UserRole.Supervisor, null);

        user.SiteId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter UserTests`
Expected: FAIL — `User` does not exist.

- [ ] **Step 3: Write the User entity**

Create `src/Bekci.Domain/Entities/User.cs`:
```csharp
namespace Bekci.Domain.Entities;

public sealed class User : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public Guid? SiteId { get; private set; }

    private User() { }

    public static User Create(Guid tenantId, string email, string passwordHash, UserRole role, Guid? siteId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email,
        PasswordHash = passwordHash,
        Role = role,
        SiteId = siteId
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter UserTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Domain/Entities/User.cs tests/Bekci.Tests/Domain/UserTests.cs
git commit -m "feat: add User domain entity"
```

---

### Task 3: Site, Route, and Checkpoint entities

**Files:**
- Create: `src/Bekci.Domain/Entities/Site.cs`, `src/Bekci.Domain/Entities/Route.cs`, `src/Bekci.Domain/Entities/Checkpoint.cs`
- Test: `tests/Bekci.Tests/Domain/RouteConfigTests.cs`

**Interfaces:**
- Consumes: `Entity`, `ITenantEntity`.
- Produces:
  - `Site.Create(Guid tenantId, string name, string? address)` → `TenantId, Name, Address`
  - `Route.Create(Guid tenantId, Guid siteId, string name, bool enforceOrder)` → `TenantId, SiteId, Name, EnforceOrder`
  - `Checkpoint.Create(Guid tenantId, Guid routeId, string name, string qrCode, double lat, double lng, double geofenceRadiusM, int sequence)` → `TenantId, RouteId, Name, QrCode, Lat, Lng, GeofenceRadiusM, Sequence`

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Domain/RouteConfigTests.cs`:
```csharp
using Bekci.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class RouteConfigTests
{
    [Fact]
    public void Site_create_sets_fields()
    {
        var tenantId = Guid.NewGuid();
        var site = Site.Create(tenantId, "Mall A", "123 Main St");

        site.TenantId.Should().Be(tenantId);
        site.Name.Should().Be("Mall A");
        site.Address.Should().Be("123 Main St");
    }

    [Fact]
    public void Route_create_sets_enforce_order()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var route = Route.Create(tenantId, siteId, "Night Loop", enforceOrder: true);

        route.SiteId.Should().Be(siteId);
        route.Name.Should().Be("Night Loop");
        route.EnforceOrder.Should().BeTrue();
    }

    [Fact]
    public void Checkpoint_create_sets_fields()
    {
        var tenantId = Guid.NewGuid();
        var routeId = Guid.NewGuid();
        var cp = Checkpoint.Create(tenantId, routeId, "Lobby", "QR-LOBBY", 40.1, 29.2, 25, 1);

        cp.RouteId.Should().Be(routeId);
        cp.QrCode.Should().Be("QR-LOBBY");
        cp.Lat.Should().Be(40.1);
        cp.Lng.Should().Be(29.2);
        cp.GeofenceRadiusM.Should().Be(25);
        cp.Sequence.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter RouteConfigTests`
Expected: FAIL — entities do not exist.

- [ ] **Step 3: Write the three entities**

Create `src/Bekci.Domain/Entities/Site.cs`:
```csharp
namespace Bekci.Domain.Entities;

public sealed class Site : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Address { get; private set; }

    private Site() { }

    public static Site Create(Guid tenantId, string name, string? address) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Address = address
    };
}
```

Create `src/Bekci.Domain/Entities/Route.cs`:
```csharp
namespace Bekci.Domain.Entities;

public sealed class Route : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid SiteId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool EnforceOrder { get; private set; }

    private Route() { }

    public static Route Create(Guid tenantId, Guid siteId, string name, bool enforceOrder) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        SiteId = siteId,
        Name = name,
        EnforceOrder = enforceOrder
    };
}
```

Create `src/Bekci.Domain/Entities/Checkpoint.cs`:
```csharp
namespace Bekci.Domain.Entities;

public sealed class Checkpoint : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid RouteId { get; private set; }
    public string Name { get; private set; } = default!;
    public string QrCode { get; private set; } = default!;
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public double GeofenceRadiusM { get; private set; }
    public int Sequence { get; private set; }

    private Checkpoint() { }

    public static Checkpoint Create(
        Guid tenantId, Guid routeId, string name, string qrCode,
        double lat, double lng, double geofenceRadiusM, int sequence) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        RouteId = routeId,
        Name = name,
        QrCode = qrCode,
        Lat = lat,
        Lng = lng,
        GeofenceRadiusM = geofenceRadiusM,
        Sequence = sequence
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter RouteConfigTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Domain/Entities tests/Bekci.Tests/Domain/RouteConfigTests.cs
git commit -m "feat: add Site, Route, Checkpoint domain entities"
```

---

### Task 4: Patrol entity

**Files:**
- Create: `src/Bekci.Domain/Entities/Patrol.cs`
- Test: `tests/Bekci.Tests/Domain/PatrolTests.cs`

**Interfaces:**
- Consumes: `Entity`, `ITenantEntity`, `PatrolStatus`.
- Produces: `Patrol.Start(Guid id, Guid tenantId, Guid routeId, Guid guardId, DateTime startedAt)`; method `Complete(DateTime completedAt)`; properties `TenantId, RouteId, GuardId, StartedAt, CompletedAt, Status`. Note `id` is **client-supplied**.

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Domain/PatrolTests.cs`:
```csharp
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class PatrolTests
{
    [Fact]
    public void Start_uses_client_supplied_id_and_is_in_progress()
    {
        var id = Guid.NewGuid();
        var startedAt = new DateTime(2026, 7, 10, 22, 0, 0, DateTimeKind.Utc);

        var patrol = Patrol.Start(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), startedAt);

        patrol.Id.Should().Be(id);
        patrol.Status.Should().Be(PatrolStatus.InProgress);
        patrol.StartedAt.Should().Be(startedAt);
        patrol.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Complete_sets_status_and_timestamp()
    {
        var patrol = Patrol.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateTime(2026, 7, 10, 22, 0, 0, DateTimeKind.Utc));
        var completedAt = new DateTime(2026, 7, 10, 22, 45, 0, DateTimeKind.Utc);

        patrol.Complete(completedAt);

        patrol.Status.Should().Be(PatrolStatus.Completed);
        patrol.CompletedAt.Should().Be(completedAt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter PatrolTests`
Expected: FAIL — `Patrol` does not exist.

- [ ] **Step 3: Write the Patrol entity**

Create `src/Bekci.Domain/Entities/Patrol.cs`:
```csharp
namespace Bekci.Domain.Entities;

public sealed class Patrol : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid GuardId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public PatrolStatus Status { get; private set; }

    private Patrol() { }

    public static Patrol Start(Guid id, Guid tenantId, Guid routeId, Guid guardId, DateTime startedAt) => new()
    {
        Id = id,
        TenantId = tenantId,
        RouteId = routeId,
        GuardId = guardId,
        StartedAt = startedAt,
        Status = PatrolStatus.InProgress
    };

    public void Complete(DateTime completedAt)
    {
        Status = PatrolStatus.Completed;
        CompletedAt = completedAt;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter PatrolTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Domain/Entities/Patrol.cs tests/Bekci.Tests/Domain/PatrolTests.cs
git commit -m "feat: add Patrol domain entity"
```

---

### Task 5: Scan entity + server-side geo/order validation

**Files:**
- Create: `src/Bekci.Domain/Entities/Scan.cs`, `src/Bekci.Domain/ScanValidation.cs`
- Test: `tests/Bekci.Tests/Domain/ScanValidationTests.cs`

**Interfaces:**
- Consumes: `Entity`, `ITenantEntity`.
- Produces:
  - `static class ScanValidation` with `static bool IsWithinGeofence(double cpLat, double cpLng, double radiusM, double? lat, double? lng)` (returns false when lat/lng null) and `static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)`.
  - `Scan.Record(Guid id, Guid tenantId, Guid patrolId, Guid checkpointId, DateTime scannedAt, DateTime receivedAt, double? lat, double? lng, bool geoValid, bool orderValid, bool isDuplicate)`; properties `TenantId, PatrolId, CheckpointId, ScannedAt, ReceivedAt, Lat, Lng, GeoValid, OrderValid, IsDuplicate`.

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Domain/ScanValidationTests.cs`:
```csharp
using Bekci.Domain;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class ScanValidationTests
{
    [Fact]
    public void Within_radius_is_valid()
    {
        // ~11 m north of the checkpoint, radius 25 m
        var ok = ScanValidation.IsWithinGeofence(40.0000, 29.0000, 25, 40.0001, 29.0000);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Outside_radius_is_invalid()
    {
        // ~111 m north, radius 25 m
        var ok = ScanValidation.IsWithinGeofence(40.0000, 29.0000, 25, 40.0010, 29.0000);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Missing_location_is_invalid()
    {
        ScanValidation.IsWithinGeofence(40.0, 29.0, 25, null, null).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter ScanValidationTests`
Expected: FAIL — `ScanValidation` does not exist.

- [ ] **Step 3: Write ScanValidation and the Scan entity**

Create `src/Bekci.Domain/ScanValidation.cs`:
```csharp
namespace Bekci.Domain;

public static class ScanValidation
{
    private const double EarthRadiusMeters = 6_371_000;

    public static bool IsWithinGeofence(double cpLat, double cpLng, double radiusM, double? lat, double? lng)
    {
        if (lat is null || lng is null)
            return false;

        return DistanceMeters(cpLat, cpLng, lat.Value, lng.Value) <= radiusM;
    }

    public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        double ToRad(double d) => d * Math.PI / 180.0;

        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }
}
```

Create `src/Bekci.Domain/Entities/Scan.cs`:
```csharp
namespace Bekci.Domain.Entities;

public sealed class Scan : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid PatrolId { get; private set; }
    public Guid CheckpointId { get; private set; }
    public DateTime ScannedAt { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public double? Lat { get; private set; }
    public double? Lng { get; private set; }
    public bool GeoValid { get; private set; }
    public bool OrderValid { get; private set; }
    public bool IsDuplicate { get; private set; }

    private Scan() { }

    public static Scan Record(
        Guid id, Guid tenantId, Guid patrolId, Guid checkpointId,
        DateTime scannedAt, DateTime receivedAt, double? lat, double? lng,
        bool geoValid, bool orderValid, bool isDuplicate) => new()
    {
        Id = id,
        TenantId = tenantId,
        PatrolId = patrolId,
        CheckpointId = checkpointId,
        ScannedAt = scannedAt,
        ReceivedAt = receivedAt,
        Lat = lat,
        Lng = lng,
        GeoValid = geoValid,
        OrderValid = orderValid,
        IsDuplicate = isDuplicate
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter ScanValidationTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Domain/ScanValidation.cs src/Bekci.Domain/Entities/Scan.cs tests/Bekci.Tests/Domain/ScanValidationTests.cs
git commit -m "feat: add Scan entity and geofence validation"
```

---

### Task 6: DbContext, EF configurations, tenant context + global query filter

**Files:**
- Create: `src/Bekci.Application/Abstractions/ITenantContext.cs`
- Create: `src/Bekci.Application/Data/Repository.cs`
- Create: `src/Bekci.Application/Data/Configurations/UserConfiguration.cs` (plus configs for other entities in the same file for brevity)
- Create: `src/Bekci.Infrastructure/DependencyInjection.cs`
- Create: `src/Bekci.Application/DependencyInjection.cs`
- Modify: `src/Bekci.Application/Bekci.Application.csproj` (add EF Core packages)
- Modify: `src/Bekci.Infrastructure/Bekci.Infrastructure.csproj` (add Npgsql + EF Design)
- Test: `tests/Bekci.Tests/Domain/TenantFilterTests.cs`

**Interfaces:**
- Produces:
  - `interface ITenantContext { Guid TenantId { get; } bool HasTenant { get; } }`
  - `class Repository(DbContextOptions<Repository> options, ITenantContext tenant) : DbContext` exposing `DbSet<Organization> Organizations`, `DbSet<User> Users`, `DbSet<Site> Sites`, `DbSet<Route> Routes`, `DbSet<Checkpoint> Checkpoints`, `DbSet<Patrol> Patrols`, `DbSet<Scan> Scans`. Global query filter `e => e.TenantId == tenant.TenantId` on every `ITenantEntity`.
  - `IServiceCollection AddInfrastructure(this IServiceCollection, IConfiguration)` and `IServiceCollection AddApplication(this IServiceCollection)`.

- [ ] **Step 1: Add EF Core packages**

Run:
```bash
cd /Users/sinanbabacan/repos/bekci_with_superpowers
dotnet add src/Bekci.Application package Microsoft.EntityFrameworkCore --version 10.0.0
dotnet add src/Bekci.Application package Microsoft.EntityFrameworkCore.Relational --version 10.0.0
dotnet add src/Bekci.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.0
dotnet add src/Bekci.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 10.0.0
dotnet add src/Bekci.Api package Microsoft.EntityFrameworkCore.Design --version 10.0.0
dotnet add tests/Bekci.Tests package Microsoft.EntityFrameworkCore.InMemory --version 10.0.0
```
Expected: packages restore. (If `10.0.0` is unavailable, use the latest `10.0.*` shown by `dotnet package search Microsoft.EntityFrameworkCore --prerelease`.)

- [ ] **Step 2: Write the failing test**

Create `tests/Bekci.Tests/Domain/TenantFilterTests.cs`:
```csharp
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bekci.Tests.Domain;

public class TenantFilterTests
{
    private sealed class FixedTenant(Guid id) : ITenantContext
    {
        public Guid TenantId => id;
        public bool HasTenant => id != Guid.Empty;
    }

    [Fact]
    public async Task Query_only_returns_current_tenant_rows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<Repository>()
            .UseInMemoryDatabase($"tenant-{Guid.NewGuid()}")
            .Options;

        // Seed with tenant A context
        await using (var db = new Repository(options, new FixedTenant(tenantA)))
        {
            db.Sites.Add(Site.Create(tenantA, "A-Site", null));
            db.Sites.Add(Site.Create(tenantB, "B-Site", null));
            await db.SaveChangesAsync();
        }

        // Read with tenant B context
        await using (var db = new Repository(options, new FixedTenant(tenantB)))
        {
            var sites = await db.Sites.ToListAsync();
            sites.Should().ContainSingle();
            sites[0].Name.Should().Be("B-Site");
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter TenantFilterTests`
Expected: FAIL — `ITenantContext` / `Repository` do not exist.

- [ ] **Step 4: Write the tenant context, DbContext, configs, and DI**

Create `src/Bekci.Application/Abstractions/ITenantContext.cs`:
```csharp
namespace Bekci.Application.Abstractions;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
}
```

Create `src/Bekci.Application/Data/Repository.cs`:
```csharp
using Bekci.Application.Abstractions;
using Bekci.Domain;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Data;

public sealed class Repository(DbContextOptions<Repository> options, ITenantContext tenant)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();
    public DbSet<Patrol> Patrols => Set<Patrol>();
    public DbSet<Scan> Scans => Set<Scan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Repository).Assembly);

        modelBuilder.Entity<User>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Site>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Route>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Checkpoint>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Patrol>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
        modelBuilder.Entity<Scan>().HasQueryFilter(e => e.TenantId == tenant.TenantId);
    }
}
```

Create `src/Bekci.Application/Data/Configurations/ModelConfiguration.cs`:
```csharp
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bekci.Application.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).IsRequired().HasMaxLength(256);
        b.Property(x => x.PasswordHash).IsRequired();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
    }
}

public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.TenantId);
    }
}

public sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(x => new { x.TenantId, x.SiteId });
    }
}

public sealed class CheckpointConfiguration : IEntityTypeConfiguration<Checkpoint>
{
    public void Configure(EntityTypeBuilder<Checkpoint> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.QrCode).IsRequired().HasMaxLength(200);
        b.HasIndex(x => new { x.RouteId, x.QrCode }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.RouteId });
    }
}

public sealed class PatrolConfiguration : IEntityTypeConfiguration<Patrol>
{
    public void Configure(EntityTypeBuilder<Patrol> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.HasIndex(x => new { x.TenantId, x.RouteId });
        b.HasIndex(x => new { x.TenantId, x.GuardId });
    }
}

public sealed class ScanConfiguration : IEntityTypeConfiguration<Scan>
{
    public void Configure(EntityTypeBuilder<Scan> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.PatrolId);
        b.HasIndex(x => new { x.PatrolId, x.CheckpointId });
    }
}

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
    }
}
```

Create `src/Bekci.Application/DependencyInjection.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Bekci.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application services are registered in later tasks.
        return services;
    }
}
```

Create `src/Bekci.Infrastructure/DependencyInjection.cs`:
```csharp
using Bekci.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bekci.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<Repository>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(DependencyInjection).Assembly.FullName)));

        return services;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter TenantFilterTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Bekci.Application src/Bekci.Infrastructure tests/Bekci.Tests/Domain/TenantFilterTests.cs
git commit -m "feat: add DbContext, EF configs, tenant query filter"
```

---

### Task 7: JWT auth, login endpoint, tenant context from claims, API wiring

**Files:**
- Create: `src/Bekci.Api/Auth/TenantContext.cs`, `src/Bekci.Api/Auth/AuthService.cs`
- Create: `src/Bekci.Application/DTOs/AuthDtos.cs`
- Create: `src/Bekci.Api/Controllers/AuthController.cs`
- Create: `src/Bekci.Api/appsettings.json` (replace generated), `src/Bekci.Api/Program.cs` (replace generated)
- Create: `tests/Bekci.Tests/Integration/ApiFactory.cs`, `tests/Bekci.Tests/Integration/AuthTests.cs`
- Modify: `src/Bekci.Api/Bekci.Api.csproj`, `tests/Bekci.Tests/Bekci.Tests.csproj` (add packages)

**Interfaces:**
- Consumes: `Repository`, `ITenantContext`, `User`, `UserRole`.
- Produces:
  - DTOs `record LoginRequest(string Email, string Password)`, `record LoginResponse(string Token, string Role, Guid TenantId)`.
  - `AuthService.LoginAsync(LoginRequest, CancellationToken) : Task<LoginResponse?>` — looks up user with `.IgnoreQueryFilters()`, verifies BCrypt, issues JWT with claims `sub`=userId, `tenant_id`=tenantId, `role`, `site_id` (if guard).
  - `TenantContext : ITenantContext` reading the `tenant_id` claim from `IHttpContextAccessor`.
  - Endpoint `POST /api/v1/auth/login`.

- [ ] **Step 1: Add auth + test packages**

Run:
```bash
cd /Users/sinanbabacan/repos/bekci_with_superpowers
dotnet add src/Bekci.Api package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.0
dotnet add src/Bekci.Api package BCrypt.Net-Next
dotnet add tests/Bekci.Tests package Microsoft.AspNetCore.Mvc.Testing --version 10.0.0
dotnet add tests/Bekci.Tests package Testcontainers.PostgreSql
```
Expected: packages restore.

- [ ] **Step 2: Write appsettings and the shared test factory**

Replace `src/Bekci.Api/appsettings.json`:
```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=bekci;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "Issuer": "bekci",
    "Audience": "bekci-clients",
    "Key": "dev-only-super-secret-key-change-me-32bytes!",
    "ExpiresInMinutes": "480"
  }
}
```

Create `tests/Bekci.Tests/Integration/ApiFactory.cs`:
```csharp
using Bekci.Application.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Bekci.Tests.Integration;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());
    }

    public async Task InitializeAsync() => await _db.StartAsync();

    public new async Task DisposeAsync() => await _db.DisposeAsync().AsTask();
}
```

> Note: `Program` must be public for `WebApplicationFactory<Program>`. Program.cs (next step) ends with `public partial class Program { }`.

- [ ] **Step 3: Write the failing test**

Create `tests/Bekci.Tests/Integration/AuthTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Domain.Entities;
using Bekci.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record LoginRequest(string Email, string Password);
    private sealed record LoginResponse(string Token, string Role, Guid TenantId);

    [Fact]
    public async Task Login_with_valid_credentials_returns_token()
    {
        var tenantId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Repository>();
            db.Database.EnsureCreated();
            db.Users.Add(User.Create(tenantId, "sup@acme.com",
                BCrypt.Net.BCrypt.HashPassword("pass123"), UserRole.Supervisor, null));
            db.SaveChanges();
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("sup@acme.com", "pass123"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Token.Should().NotBeNullOrEmpty();
        body.Role.Should().Be("Supervisor");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("sup@acme.com", "wrong"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter AuthTests`
Expected: FAIL — `Program`/auth endpoint/`TenantContext` do not exist (compile error).

- [ ] **Step 5: Write DTOs, TenantContext, AuthService, AuthController, and Program.cs**

Create `src/Bekci.Application/DTOs/AuthDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, string Role, Guid TenantId);
```

Create `src/Bekci.Api/Auth/TenantContext.cs`:
```csharp
using System.Security.Claims;
using Bekci.Application.Abstractions;

namespace Bekci.Api.Auth;

public sealed class TenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var claim = accessor.HttpContext?.User.FindFirstValue("tenant_id");
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }

    public bool HasTenant => TenantId != Guid.Empty;
}
```

Create `src/Bekci.Api/Auth/AuthService.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Bekci.Api.Auth;

public sealed class AuthService(Repository repository, IConfiguration configuration)
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        // Login must find the user before a tenant is known → bypass the tenant filter.
        var user = await repository.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == request.Email, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        var jwt = configuration.GetSection("Jwt");
        var expires = int.Parse(jwt["ExpiresInMinutes"] ?? "480");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (user.SiteId is not null)
            claims.Add(new Claim("site_id", user.SiteId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expires),
            signingCredentials: creds);

        return new LoginResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            user.Role.ToString(),
            user.TenantId);
    }
}
```

Create `src/Bekci.Api/Controllers/AuthController.cs`:
```csharp
using Bekci.Api.Auth;
using Bekci.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var response = await authService.LoginAsync(request, ct);
        return response is null
            ? Unauthorized(new { error = "Invalid email or password." })
            : Ok(response);
    }
}
```

Replace `src/Bekci.Api/Program.cs`:
```csharp
using System.Text;
using Bekci.Api.Auth;
using Bekci.Application;
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<AuthService>();

var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Repository>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
```

> The Program.cs migration call uses `Migrate()`; the integration test seeds via `EnsureCreated()` against a fresh Testcontainer, so no migration is required for tests. Migrations are generated in Task 8.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter AuthTests`
Expected: PASS (Docker must be running for Testcontainers).

- [ ] **Step 7: Commit**

```bash
git add src/Bekci.Api src/Bekci.Application/DTOs/AuthDtos.cs tests/Bekci.Tests/Integration
git commit -m "feat: JWT auth, login endpoint, tenant context from claims"
```

---

### Task 8: Initial EF migration + a `CurrentUser` helper

**Files:**
- Create: `src/Bekci.Infrastructure/Migrations/*` (generated)
- Create: `src/Bekci.Api/Auth/CurrentUser.cs`
- Modify: `src/Bekci.Api/Program.cs` (register `ICurrentUser`)
- Test: `tests/Bekci.Tests/Integration/MigrationTests.cs`

**Interfaces:**
- Produces: `interface ICurrentUser { Guid UserId { get; } Guid TenantId { get; } string Role { get; } Guid? SiteId { get; } }` and its `CurrentUser` implementation reading claims. Used by every service in later tasks to stamp `TenantId` and identify the guard.

- [ ] **Step 1: Generate the initial migration**

Run:
```bash
cd /Users/sinanbabacan/repos/bekci_with_superpowers
dotnet tool install --global dotnet-ef --version 10.0.0 2>/dev/null || true
dotnet ef migrations add InitialCreate \
  --project src/Bekci.Infrastructure \
  --startup-project src/Bekci.Api \
  --output-dir Migrations
```
Expected: a migration is created under `src/Bekci.Infrastructure/Migrations`. (Design-time uses the `ITenantContext`; if the design-time factory errors, add a `DesignTimeTenantContext` returning `Guid.Empty` — but query filters do not affect migration generation, so this should succeed.)

- [ ] **Step 2: Write the failing test**

Create `tests/Bekci.Tests/Integration/MigrationTests.cs`:
```csharp
using Bekci.Application.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class MigrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Database_migrates_and_has_scans_table()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Repository>();
        await db.Database.MigrateAsync();

        var pending = await db.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run test to verify it fails or passes**

Run: `dotnet test tests/Bekci.Tests --filter MigrationTests`
Expected: PASS once the migration exists (this test guards against forgetting to regenerate migrations after model changes).

- [ ] **Step 4: Add the CurrentUser helper**

Create `src/Bekci.Api/Auth/CurrentUser.cs`:
```csharp
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Bekci.Api.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    string Role { get; }
    Guid? SiteId { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid UserId => Guid.TryParse(User?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
    public Guid TenantId => Guid.TryParse(User?.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;
    public string Role => User?.FindFirstValue(ClaimTypes.Role) ?? "";
    public Guid? SiteId => Guid.TryParse(User?.FindFirstValue("site_id"), out var id) ? id : null;
}
```

Modify `src/Bekci.Api/Program.cs` — add after the `TenantContext` registration line (`builder.Services.AddScoped<ITenantContext, TenantContext>();`):
```csharp
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test tests/Bekci.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Bekci.Infrastructure/Migrations src/Bekci.Api/Auth/CurrentUser.cs src/Bekci.Api/Program.cs tests/Bekci.Tests/Integration/MigrationTests.cs
git commit -m "feat: initial EF migration + current-user claim helper"
```

---

### Task 9: Sites CRUD (supervisor)

**Files:**
- Create: `src/Bekci.Application/DTOs/SiteDtos.cs`, `src/Bekci.Application/Services/SiteService.cs`
- Create: `src/Bekci.Api/Controllers/SitesController.cs`
- Modify: `src/Bekci.Application/DependencyInjection.cs` (register `SiteService`)
- Test: `tests/Bekci.Tests/Integration/SitesTests.cs`, `tests/Bekci.Tests/Integration/AuthHelper.cs`

**Interfaces:**
- Consumes: `Repository`, `ICurrentUser`, `Site`.
- Produces:
  - DTOs `record CreateSiteRequest(string Name, string? Address)`, `record SiteResponse(Guid Id, string Name, string? Address)`.
  - `SiteService` with `CreateAsync(CreateSiteRequest, CancellationToken) : Task<SiteResponse>`, `ListAsync(CancellationToken) : Task<IReadOnlyList<SiteResponse>>`.
  - Endpoints `POST /api/v1/sites`, `GET /api/v1/sites` (role `Supervisor`).
  - Test helper `AuthHelper.SeedUserAndLoginAsync(...)` reused by later tasks.

- [ ] **Step 1: Add the reusable auth test helper**

Create `tests/Bekci.Tests/Integration/AuthHelper.cs`:
```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Bekci.Tests.Integration;

public static class AuthHelper
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record LoginResponse(string Token, string Role, Guid TenantId);

    /// Seeds a user of the given role and returns an HttpClient with the bearer token attached.
    public static async Task<(HttpClient Client, Guid TenantId)> LoginAsAsync(
        ApiFactory factory, UserRole role, Guid tenantId, Guid? siteId = null, string? email = null)
    {
        email ??= $"{role}-{Guid.NewGuid():N}@acme.com";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Repository>();
            db.Database.EnsureCreated();
            db.Users.Add(User.Create(tenantId, email,
                BCrypt.Net.BCrypt.HashPassword("pass123"), role, siteId));
            db.SaveChanges();
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "pass123"));
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return (client, tenantId);
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/Bekci.Tests/Integration/SitesTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Bekci.Domain;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Integration;

public class SitesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record CreateSiteRequest(string Name, string? Address);
    private sealed record SiteResponse(Guid Id, string Name, string? Address);

    [Fact]
    public async Task Supervisor_can_create_and_list_sites()
    {
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Supervisor, Guid.NewGuid());

        var create = await client.PostAsJsonAsync("/api/v1/sites", new CreateSiteRequest("Mall A", "123 Main"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<List<SiteResponse>>("/api/v1/sites");
        list.Should().ContainSingle(s => s.Name == "Mall A");
    }

    [Fact]
    public async Task Sites_are_isolated_between_tenants()
    {
        var (clientA, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Supervisor, Guid.NewGuid());
        await clientA.PostAsJsonAsync("/api/v1/sites", new CreateSiteRequest("A-Site", null));

        var (clientB, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Supervisor, Guid.NewGuid());
        var listB = await clientB.GetFromJsonAsync<List<SiteResponse>>("/api/v1/sites");

        listB.Should().BeEmpty();
    }

    [Fact]
    public async Task Guard_cannot_create_sites()
    {
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Guard, Guid.NewGuid(), Guid.NewGuid());
        var resp = await client.PostAsJsonAsync("/api/v1/sites", new CreateSiteRequest("X", null));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter SitesTests`
Expected: FAIL — sites endpoint does not exist.

- [ ] **Step 4: Write DTOs, service, controller, and registration**

Create `src/Bekci.Application/DTOs/SiteDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record CreateSiteRequest(string Name, string? Address);
public sealed record SiteResponse(Guid Id, string Name, string? Address);
```

Create `src/Bekci.Application/Services/SiteService.cs`:
```csharp
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class SiteService(Repository db, ITenantContext tenant)
{
    public async Task<SiteResponse> CreateAsync(CreateSiteRequest req, CancellationToken ct)
    {
        var site = Site.Create(tenant.TenantId, req.Name, req.Address);
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return new SiteResponse(site.Id, site.Name, site.Address);
    }

    public async Task<IReadOnlyList<SiteResponse>> ListAsync(CancellationToken ct) =>
        await db.Sites
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SiteResponse(s.Id, s.Name, s.Address))
            .ToListAsync(ct);
}
```

> `ITenantContext.TenantId` supplies the tenant on writes; the global query filter scopes reads.

Create `src/Bekci.Api/Controllers/SitesController.cs`:
```csharp
using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/sites")]
[Authorize(Roles = "Supervisor")]
public sealed class SitesController(SiteService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSiteRequest req, CancellationToken ct)
    {
        var site = await service.CreateAsync(req, ct);
        return CreatedAtAction(nameof(List), new { }, site);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(ct));
}
```

Replace `src/Bekci.Application/DependencyInjection.cs`:
```csharp
using Bekci.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bekci.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SiteService>();
        return services;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter SitesTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Bekci.Application src/Bekci.Api/Controllers/SitesController.cs tests/Bekci.Tests/Integration
git commit -m "feat: sites CRUD with tenant isolation and role checks"
```

---

### Task 10: Routes CRUD (supervisor)

**Files:**
- Create: `src/Bekci.Application/DTOs/RouteDtos.cs`, `src/Bekci.Application/Services/RouteService.cs`
- Create: `src/Bekci.Api/Controllers/RoutesController.cs`
- Modify: `src/Bekci.Application/DependencyInjection.cs` (register `RouteService`)
- Test: `tests/Bekci.Tests/Integration/RoutesTests.cs`

**Interfaces:**
- Consumes: `Repository`, `ITenantContext`, `Route`, `Site`.
- Produces:
  - DTOs `record CreateRouteRequest(Guid SiteId, string Name, bool EnforceOrder)`, `record RouteResponse(Guid Id, Guid SiteId, string Name, bool EnforceOrder)`.
  - `RouteService.CreateAsync`, `ListBySiteAsync(Guid siteId, CancellationToken)`.
  - Endpoints `POST /api/v1/routes`, `GET /api/v1/routes?siteId=` (role `Supervisor`).

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Integration/RoutesTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Bekci.Domain;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Integration;

public class RoutesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record CreateSiteRequest(string Name, string? Address);
    private sealed record SiteResponse(Guid Id, string Name, string? Address);
    private sealed record CreateRouteRequest(Guid SiteId, string Name, bool EnforceOrder);
    private sealed record RouteResponse(Guid Id, Guid SiteId, string Name, bool EnforceOrder);

    [Fact]
    public async Task Supervisor_creates_route_under_site()
    {
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Supervisor, Guid.NewGuid());
        var site = await (await client.PostAsJsonAsync("/api/v1/sites",
            new CreateSiteRequest("Mall A", null))).Content.ReadFromJsonAsync<SiteResponse>();

        var create = await client.PostAsJsonAsync("/api/v1/routes",
            new CreateRouteRequest(site!.Id, "Night Loop", true));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var routes = await client.GetFromJsonAsync<List<RouteResponse>>($"/api/v1/routes?siteId={site.Id}");
        routes.Should().ContainSingle(r => r.Name == "Night Loop" && r.EnforceOrder);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter RoutesTests`
Expected: FAIL — routes endpoint does not exist.

- [ ] **Step 3: Write DTOs, service, controller, registration**

Create `src/Bekci.Application/DTOs/RouteDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record CreateRouteRequest(Guid SiteId, string Name, bool EnforceOrder);
public sealed record RouteResponse(Guid Id, Guid SiteId, string Name, bool EnforceOrder);
```

Create `src/Bekci.Application/Services/RouteService.cs`:
```csharp
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class RouteService(Repository db, ITenantContext tenant)
{
    public async Task<RouteResponse> CreateAsync(CreateRouteRequest req, CancellationToken ct)
    {
        var route = Route.Create(tenant.TenantId, req.SiteId, req.Name, req.EnforceOrder);
        db.Routes.Add(route);
        await db.SaveChangesAsync(ct);
        return new RouteResponse(route.Id, route.SiteId, route.Name, route.EnforceOrder);
    }

    public async Task<IReadOnlyList<RouteResponse>> ListBySiteAsync(Guid siteId, CancellationToken ct) =>
        await db.Routes
            .AsNoTracking()
            .Where(r => r.SiteId == siteId)
            .OrderBy(r => r.Name)
            .Select(r => new RouteResponse(r.Id, r.SiteId, r.Name, r.EnforceOrder))
            .ToListAsync(ct);
}
```

Create `src/Bekci.Api/Controllers/RoutesController.cs`:
```csharp
using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/routes")]
[Authorize(Roles = "Supervisor")]
public sealed class RoutesController(RouteService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRouteRequest req, CancellationToken ct)
    {
        var route = await service.CreateAsync(req, ct);
        return CreatedAtAction(nameof(ListBySite), new { siteId = route.SiteId }, route);
    }

    [HttpGet]
    public async Task<IActionResult> ListBySite([FromQuery] Guid siteId, CancellationToken ct) =>
        Ok(await service.ListBySiteAsync(siteId, ct));
}
```

Modify `src/Bekci.Application/DependencyInjection.cs` — add inside `AddApplication`, after `services.AddScoped<SiteService>();`:
```csharp
        services.AddScoped<RouteService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter RoutesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Application src/Bekci.Api/Controllers/RoutesController.cs tests/Bekci.Tests/Integration/RoutesTests.cs
git commit -m "feat: routes CRUD under sites"
```

---

### Task 11: Checkpoints CRUD (supervisor)

**Files:**
- Create: `src/Bekci.Application/DTOs/CheckpointDtos.cs`, `src/Bekci.Application/Services/CheckpointService.cs`
- Create: `src/Bekci.Api/Controllers/CheckpointsController.cs`
- Modify: `src/Bekci.Application/DependencyInjection.cs` (register `CheckpointService`)
- Test: `tests/Bekci.Tests/Integration/CheckpointsTests.cs`

**Interfaces:**
- Consumes: `Repository`, `ITenantContext`, `Checkpoint`.
- Produces:
  - DTOs `record CreateCheckpointRequest(string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence)`, `record CheckpointResponse(Guid Id, Guid RouteId, string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence)`.
  - `CheckpointService.AddAsync(Guid routeId, CreateCheckpointRequest, CancellationToken)`, `ListByRouteAsync(Guid routeId, CancellationToken)` (ordered by `Sequence`).
  - Endpoints `POST /api/v1/routes/{routeId}/checkpoints`, `GET /api/v1/routes/{routeId}/checkpoints` (role `Supervisor`).

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Integration/CheckpointsTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Bekci.Domain;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Integration;

public class CheckpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record CreateSiteRequest(string Name, string? Address);
    private sealed record SiteResponse(Guid Id, string Name, string? Address);
    private sealed record CreateRouteRequest(Guid SiteId, string Name, bool EnforceOrder);
    private sealed record RouteResponse(Guid Id, Guid SiteId, string Name, bool EnforceOrder);
    private sealed record CreateCheckpointRequest(string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence);
    private sealed record CheckpointResponse(Guid Id, Guid RouteId, string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence);

    [Fact]
    public async Task Supervisor_adds_checkpoints_returned_in_sequence()
    {
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Supervisor, Guid.NewGuid());
        var site = await (await client.PostAsJsonAsync("/api/v1/sites",
            new CreateSiteRequest("Mall A", null))).Content.ReadFromJsonAsync<SiteResponse>();
        var route = await (await client.PostAsJsonAsync("/api/v1/routes",
            new CreateRouteRequest(site!.Id, "Loop", true))).Content.ReadFromJsonAsync<RouteResponse>();

        await client.PostAsJsonAsync($"/api/v1/routes/{route!.Id}/checkpoints",
            new CreateCheckpointRequest("Back Door", "QR-2", 40.1, 29.1, 25, 2));
        var second = await client.PostAsJsonAsync($"/api/v1/routes/{route.Id}/checkpoints",
            new CreateCheckpointRequest("Lobby", "QR-1", 40.0, 29.0, 25, 1));
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<List<CheckpointResponse>>($"/api/v1/routes/{route.Id}/checkpoints");
        list.Should().HaveCount(2);
        list![0].Sequence.Should().Be(1);
        list[1].Sequence.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter CheckpointsTests`
Expected: FAIL — checkpoints endpoint does not exist.

- [ ] **Step 3: Write DTOs, service, controller, registration**

Create `src/Bekci.Application/DTOs/CheckpointDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record CreateCheckpointRequest(
    string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence);

public sealed record CheckpointResponse(
    Guid Id, Guid RouteId, string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence);
```

Create `src/Bekci.Application/Services/CheckpointService.cs`:
```csharp
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class CheckpointService(Repository db, ITenantContext tenant)
{
    public async Task<CheckpointResponse> AddAsync(Guid routeId, CreateCheckpointRequest req, CancellationToken ct)
    {
        var cp = Checkpoint.Create(tenant.TenantId, routeId, req.Name, req.QrCode,
            req.Lat, req.Lng, req.GeofenceRadiusM, req.Sequence);
        db.Checkpoints.Add(cp);
        await db.SaveChangesAsync(ct);
        return Map(cp);
    }

    public async Task<IReadOnlyList<CheckpointResponse>> ListByRouteAsync(Guid routeId, CancellationToken ct) =>
        await db.Checkpoints
            .AsNoTracking()
            .Where(c => c.RouteId == routeId)
            .OrderBy(c => c.Sequence)
            .Select(c => new CheckpointResponse(c.Id, c.RouteId, c.Name, c.QrCode, c.Lat, c.Lng, c.GeofenceRadiusM, c.Sequence))
            .ToListAsync(ct);

    private static CheckpointResponse Map(Checkpoint c) =>
        new(c.Id, c.RouteId, c.Name, c.QrCode, c.Lat, c.Lng, c.GeofenceRadiusM, c.Sequence);
}
```

Create `src/Bekci.Api/Controllers/CheckpointsController.cs`:
```csharp
using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/routes/{routeId:guid}/checkpoints")]
[Authorize(Roles = "Supervisor")]
public sealed class CheckpointsController(CheckpointService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add(Guid routeId, [FromBody] CreateCheckpointRequest req, CancellationToken ct)
    {
        var cp = await service.AddAsync(routeId, req, ct);
        return CreatedAtAction(nameof(List), new { routeId }, cp);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid routeId, CancellationToken ct) =>
        Ok(await service.ListByRouteAsync(routeId, ct));
}
```

Modify `src/Bekci.Application/DependencyInjection.cs` — add after `services.AddScoped<RouteService>();`:
```csharp
        services.AddScoped<CheckpointService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter CheckpointsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Application src/Bekci.Api/Controllers/CheckpointsController.cs tests/Bekci.Tests/Integration/CheckpointsTests.cs
git commit -m "feat: checkpoints CRUD ordered by sequence"
```

---

### Task 12: Guard — list routes for their site + start a patrol

**Files:**
- Create: `src/Bekci.Application/DTOs/PatrolDtos.cs`, `src/Bekci.Application/Services/PatrolService.cs`
- Create: `src/Bekci.Api/Controllers/GuardRoutesController.cs`, `src/Bekci.Api/Controllers/PatrolsController.cs`
- Modify: `src/Bekci.Application/DependencyInjection.cs` (register `PatrolService`)
- Test: `tests/Bekci.Tests/Integration/PatrolStartTests.cs`

**Interfaces:**
- Consumes: `Repository`, `ICurrentUser`, `ITenantContext`, `Route`, `Patrol`.
- Produces:
  - DTOs `record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt)`, `record PatrolResponse(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status)`.
  - `PatrolService.ListRoutesForGuardAsync(CancellationToken)` (routes at the guard's `SiteId`), `StartAsync(StartPatrolRequest, CancellationToken)` — idempotent on `PatrolId`.
  - Endpoints `GET /api/v1/guard/routes`, `POST /api/v1/patrols` (role `Guard`).

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Integration/PatrolStartTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class PatrolStartTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record RouteResponse(Guid Id, Guid SiteId, string Name, bool EnforceOrder);
    private sealed record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt);
    private sealed record PatrolResponse(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status);

    private async Task<(Guid tenantId, Guid siteId, Guid routeId)> SeedSiteAndRoute()
    {
        var tenantId = Guid.NewGuid();
        var site = Site.Create(tenantId, "Mall A", null);
        var route = Route.Create(tenantId, site.Id, "Loop", enforceOrder: false);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Repository>();
        db.Database.EnsureCreated();
        db.Sites.Add(site);
        db.Routes.Add(route);
        await db.SaveChangesAsync();
        return (tenantId, site.Id, route.Id);
    }

    [Fact]
    public async Task Guard_sees_routes_at_their_site_and_can_start_patrol()
    {
        var (tenantId, siteId, routeId) = await SeedSiteAndRoute();
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Guard, tenantId, siteId);

        var routes = await client.GetFromJsonAsync<List<RouteResponse>>("/api/v1/guard/routes");
        routes.Should().ContainSingle(r => r.Id == routeId);

        var patrolId = Guid.NewGuid();
        var start = await client.PostAsJsonAsync("/api/v1/patrols",
            new StartPatrolRequest(patrolId, routeId, DateTime.UtcNow));
        start.StatusCode.Should().Be(HttpStatusCode.Created);
        var patrol = await start.Content.ReadFromJsonAsync<PatrolResponse>();
        patrol!.Id.Should().Be(patrolId);
        patrol.Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task Starting_same_patrol_id_twice_is_idempotent()
    {
        var (tenantId, siteId, routeId) = await SeedSiteAndRoute();
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Guard, tenantId, siteId);

        var patrolId = Guid.NewGuid();
        var req = new StartPatrolRequest(patrolId, routeId, DateTime.UtcNow);
        var first = await client.PostAsJsonAsync("/api/v1/patrols", req);
        var second = await client.PostAsJsonAsync("/api/v1/patrols", req);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created); // no error, same patrol
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter PatrolStartTests`
Expected: FAIL — patrol endpoints do not exist.

- [ ] **Step 3: Write DTOs, service, controllers, registration**

Create `src/Bekci.Application/DTOs/PatrolDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt);
public sealed record PatrolResponse(
    Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status);
```

Create `src/Bekci.Application/Services/PatrolService.cs`:
```csharp
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class PatrolService(Repository db, ITenantContext tenant)
{
    /// The current guard's site id is passed in from the controller (claim-sourced).
    public async Task<IReadOnlyList<RouteResponse>> ListRoutesForGuardAsync(Guid? guardSiteId, CancellationToken ct)
    {
        if (guardSiteId is null)
            return [];

        return await db.Routes
            .AsNoTracking()
            .Where(r => r.SiteId == guardSiteId)
            .OrderBy(r => r.Name)
            .Select(r => new RouteResponse(r.Id, r.SiteId, r.Name, r.EnforceOrder))
            .ToListAsync(ct);
    }

    public async Task<PatrolResponse> StartAsync(StartPatrolRequest req, Guid guardId, CancellationToken ct)
    {
        var existing = await db.Patrols.FirstOrDefaultAsync(p => p.Id == req.PatrolId, ct);
        if (existing is not null)
            return Map(existing); // idempotent

        var patrol = Patrol.Start(req.PatrolId, tenant.TenantId, req.RouteId, guardId,
            DateTime.SpecifyKind(req.StartedAt, DateTimeKind.Utc));
        db.Patrols.Add(patrol);
        await db.SaveChangesAsync(ct);
        return Map(patrol);
    }

    private static PatrolResponse Map(Patrol p) =>
        new(p.Id, p.RouteId, p.GuardId, p.StartedAt, p.CompletedAt, p.Status.ToString());
}
```

Create `src/Bekci.Api/Controllers/GuardRoutesController.cs`:
```csharp
using Bekci.Api.Auth;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/guard/routes")]
[Authorize(Roles = "Guard")]
public sealed class GuardRoutesController(PatrolService service, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await service.ListRoutesForGuardAsync(currentUser.SiteId, ct));
}
```

Create `src/Bekci.Api/Controllers/PatrolsController.cs`:
```csharp
using Bekci.Api.Auth;
using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/patrols")]
[Authorize(Roles = "Guard")]
public sealed class PatrolsController(PatrolService service, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartPatrolRequest req, CancellationToken ct)
    {
        var patrol = await service.StartAsync(req, currentUser.UserId, ct);
        return CreatedAtAction(nameof(Start), new { id = patrol.Id }, patrol);
    }
}
```

Modify `src/Bekci.Application/DependencyInjection.cs` — add after `services.AddScoped<CheckpointService>();`:
```csharp
        services.AddScoped<PatrolService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter PatrolStartTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Application src/Bekci.Api/Controllers/GuardRoutesController.cs src/Bekci.Api/Controllers/PatrolsController.cs tests/Bekci.Tests/Integration/PatrolStartTests.cs
git commit -m "feat: guard route listing and idempotent patrol start"
```

---

### Task 13: Scan ingestion — batch, idempotent, server re-validation (critical endpoint)

**Files:**
- Create: `src/Bekci.Application/DTOs/ScanDtos.cs`, `src/Bekci.Application/Services/ScanService.cs`
- Modify: `src/Bekci.Api/Controllers/PatrolsController.cs` (add `POST {id}/scans`)
- Modify: `src/Bekci.Application/DependencyInjection.cs` (register `ScanService`)
- Test: `tests/Bekci.Tests/Integration/ScanIngestionTests.cs`

**Interfaces:**
- Consumes: `Repository`, `ITenantContext`, `Checkpoint`, `Patrol`, `Scan`, `ScanValidation`.
- Produces:
  - DTOs `record ScanInput(Guid ScanId, Guid CheckpointId, DateTime ScannedAt, double? Lat, double? Lng)`, `record IngestScansRequest(List<ScanInput> Scans)`, `record ScanResult(Guid ScanId, bool GeoValid, bool OrderValid, bool IsDuplicate)`, `record IngestScansResponse(List<ScanResult> Results)`.
  - `ScanService.IngestAsync(Guid patrolId, IngestScansRequest, CancellationToken)` — for each input: skip (return existing verdict) if `ScanId` already stored; else load checkpoint, compute `GeoValid` via `ScanValidation.IsWithinGeofence`, compute `OrderValid` (respect the route's `EnforceOrder`), set `IsDuplicate` if the checkpoint was already scanned in this patrol, persist with server `ReceivedAt`.
  - Endpoint `POST /api/v1/patrols/{id}/scans` (role `Guard`).

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Integration/ScanIngestionTests.cs`:
```csharp
using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class ScanIngestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt);
    private sealed record ScanInput(Guid ScanId, Guid CheckpointId, DateTime ScannedAt, double? Lat, double? Lng);
    private sealed record IngestScansRequest(List<ScanInput> Scans);
    private sealed record ScanResult(Guid ScanId, bool GeoValid, bool OrderValid, bool IsDuplicate);
    private sealed record IngestScansResponse(List<ScanResult> Results);

    private sealed record Seeded(Guid TenantId, Guid SiteId, Guid RouteId, Guid Cp1, Guid Cp2);

    private async Task<Seeded> SeedOrderedRoute()
    {
        var tenantId = Guid.NewGuid();
        var site = Site.Create(tenantId, "Mall A", null);
        var route = Route.Create(tenantId, site.Id, "Loop", enforceOrder: true);
        var cp1 = Checkpoint.Create(tenantId, route.Id, "Lobby", "QR-1", 40.0000, 29.0000, 25, 1);
        var cp2 = Checkpoint.Create(tenantId, route.Id, "Back", "QR-2", 41.0000, 30.0000, 25, 2);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Repository>();
        db.Database.EnsureCreated();
        db.Sites.Add(site); db.Routes.Add(route);
        db.Checkpoints.AddRange(cp1, cp2);
        await db.SaveChangesAsync();
        return new Seeded(tenantId, site.Id, route.Id, cp1.Id, cp2.Id);
    }

    [Fact]
    public async Task Ingest_computes_geo_and_order_flags_and_dedupes()
    {
        var s = await SeedOrderedRoute();
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Guard, s.TenantId, s.SiteId);

        var patrolId = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/v1/patrols", new StartPatrolRequest(patrolId, s.RouteId, DateTime.UtcNow));

        // Scan cp2 FIRST (out of order) with good GPS, then cp1 with NO gps.
        var scanA = new ScanInput(Guid.NewGuid(), s.Cp2, DateTime.UtcNow, 41.00005, 30.00000); // within 25m of cp2
        var scanB = new ScanInput(Guid.NewGuid(), s.Cp1, DateTime.UtcNow, null, null);          // no gps

        var resp = await client.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/scans",
            new IngestScansRequest([scanA, scanB]));
        var body = await resp.Content.ReadFromJsonAsync<IngestScansResponse>();

        var rA = body!.Results.Single(r => r.ScanId == scanA.ScanId);
        rA.GeoValid.Should().BeTrue();
        rA.OrderValid.Should().BeFalse(); // cp2 scanned before cp1 on an ordered route

        var rB = body.Results.Single(r => r.ScanId == scanB.ScanId);
        rB.GeoValid.Should().BeFalse();   // no gps
        rB.OrderValid.Should().BeFalse(); // cp1 (seq 1) after cp2 already scanned → not the next expected

        // Re-post scanA → idempotent, still exactly one stored scan for that id.
        var resp2 = await client.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/scans",
            new IngestScansRequest([scanA]));
        resp2.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Repository>();
        db.Scans.IgnoreQueryFilters().Count(x => x.Id == scanA.ScanId).Should().Be(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter ScanIngestionTests`
Expected: FAIL — scans endpoint does not exist.

- [ ] **Step 3: Write DTOs, ScanService, controller action, registration**

Create `src/Bekci.Application/DTOs/ScanDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record ScanInput(Guid ScanId, Guid CheckpointId, DateTime ScannedAt, double? Lat, double? Lng);
public sealed record IngestScansRequest(List<ScanInput> Scans);
public sealed record ScanResult(Guid ScanId, bool GeoValid, bool OrderValid, bool IsDuplicate);
public sealed record IngestScansResponse(List<ScanResult> Results);
```

Create `src/Bekci.Application/Services/ScanService.cs`:
```csharp
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class ScanService(Repository db, ITenantContext tenant)
{
    public async Task<IngestScansResponse> IngestAsync(Guid patrolId, IngestScansRequest req, CancellationToken ct)
    {
        var patrol = await db.Patrols.FirstOrDefaultAsync(p => p.Id == patrolId, ct)
            ?? throw new InvalidOperationException("Patrol not found.");

        var route = await db.Routes.FirstAsync(r => r.Id == patrol.RouteId, ct);
        var checkpoints = await db.Checkpoints
            .Where(c => c.RouteId == route.Id)
            .OrderBy(c => c.Sequence)
            .ToListAsync(ct);

        // Existing scans for this patrol (source of truth for dedupe + ordering).
        var existing = await db.Scans.Where(x => x.PatrolId == patrolId).ToListAsync(ct);
        var results = new List<ScanResult>();

        // Process inputs in ScannedAt order so ordering verdicts are deterministic.
        foreach (var input in req.Scans.OrderBy(x => x.ScannedAt))
        {
            var already = existing.FirstOrDefault(x => x.Id == input.ScanId);
            if (already is not null)
            {
                results.Add(new ScanResult(already.Id, already.GeoValid, already.OrderValid, already.IsDuplicate));
                continue;
            }

            var cp = checkpoints.FirstOrDefault(c => c.Id == input.CheckpointId);
            if (cp is null)
                throw new InvalidOperationException("Checkpoint is not part of this patrol's route.");

            var geoValid = ScanValidation.IsWithinGeofence(cp.Lat, cp.Lng, cp.GeofenceRadiusM, input.Lat, input.Lng);

            var alreadyScannedThisCp = existing.Any(x => x.CheckpointId == cp.Id);
            var isDuplicate = alreadyScannedThisCp;

            bool orderValid;
            if (!route.EnforceOrder)
            {
                orderValid = true;
            }
            else
            {
                // Next expected = first checkpoint by sequence not yet scanned.
                var scannedCheckpointIds = existing.Select(x => x.CheckpointId).ToHashSet();
                var nextExpected = checkpoints.FirstOrDefault(c => !scannedCheckpointIds.Contains(c.Id));
                orderValid = nextExpected is not null && nextExpected.Id == cp.Id;
            }

            var scan = Scan.Record(
                input.ScanId, tenant.TenantId, patrolId, cp.Id,
                DateTime.SpecifyKind(input.ScannedAt, DateTimeKind.Utc),
                DateTime.UtcNow, input.Lat, input.Lng, geoValid, orderValid, isDuplicate);

            db.Scans.Add(scan);
            existing.Add(scan); // affects ordering/dedupe of subsequent inputs in the same batch
            results.Add(new ScanResult(scan.Id, geoValid, orderValid, isDuplicate));
        }

        await db.SaveChangesAsync(ct);
        return new IngestScansResponse(results);
    }
}
```

Modify `src/Bekci.Api/Controllers/PatrolsController.cs` — add the `ScanService` to the primary constructor and add the action. The full file becomes:
```csharp
using Bekci.Api.Auth;
using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/patrols")]
[Authorize(Roles = "Guard")]
public sealed class PatrolsController(
    PatrolService service, ScanService scanService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartPatrolRequest req, CancellationToken ct)
    {
        var patrol = await service.StartAsync(req, currentUser.UserId, ct);
        return CreatedAtAction(nameof(Start), new { id = patrol.Id }, patrol);
    }

    [HttpPost("{id:guid}/scans")]
    public async Task<IActionResult> Scans(Guid id, [FromBody] IngestScansRequest req, CancellationToken ct)
    {
        var result = await scanService.IngestAsync(id, req, ct);
        return Ok(result);
    }
}
```

Modify `src/Bekci.Application/DependencyInjection.cs` — add after `services.AddScoped<PatrolService>();`:
```csharp
        services.AddScoped<ScanService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter ScanIngestionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Application src/Bekci.Api/Controllers/PatrolsController.cs tests/Bekci.Tests/Integration/ScanIngestionTests.cs
git commit -m "feat: idempotent batch scan ingestion with server-side geo/order validation"
```

---

### Task 14: Complete a patrol (guard)

**Files:**
- Create: `src/Bekci.Application/DTOs/CompletePatrolDtos.cs`
- Modify: `src/Bekci.Application/Services/PatrolService.cs` (add `CompleteAsync`)
- Modify: `src/Bekci.Api/Controllers/PatrolsController.cs` (add `POST {id}/complete`)
- Test: `tests/Bekci.Tests/Integration/PatrolCompleteTests.cs`

**Interfaces:**
- Consumes: `Repository`, `Patrol`, `PatrolService`.
- Produces:
  - DTO `record CompletePatrolRequest(DateTime CompletedAt)`.
  - `PatrolService.CompleteAsync(Guid patrolId, DateTime completedAt, CancellationToken) : Task<PatrolResponse?>` — idempotent (completing an already-completed patrol returns it unchanged).
  - Endpoint `POST /api/v1/patrols/{id}/complete` (role `Guard`).

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Integration/PatrolCompleteTests.cs`:
```csharp
using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class PatrolCompleteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt);
    private sealed record CompletePatrolRequest(DateTime CompletedAt);
    private sealed record PatrolResponse(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status);

    [Fact]
    public async Task Guard_completes_patrol()
    {
        var tenantId = Guid.NewGuid();
        var site = Site.Create(tenantId, "Mall A", null);
        var route = Route.Create(tenantId, site.Id, "Loop", false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Repository>();
            db.Database.EnsureCreated();
            db.Sites.Add(site); db.Routes.Add(route);
            await db.SaveChangesAsync();
        }
        var (client, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Guard, tenantId, site.Id);

        var patrolId = Guid.NewGuid();
        await client.PostAsJsonAsync("/api/v1/patrols", new StartPatrolRequest(patrolId, route.Id, DateTime.UtcNow));

        var resp = await client.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/complete",
            new CompletePatrolRequest(DateTime.UtcNow));
        resp.EnsureSuccessStatusCode();
        var patrol = await resp.Content.ReadFromJsonAsync<PatrolResponse>();
        patrol!.Status.Should().Be("Completed");
        patrol.CompletedAt.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter PatrolCompleteTests`
Expected: FAIL — complete endpoint does not exist.

- [ ] **Step 3: Write DTO, service method, controller action**

Create `src/Bekci.Application/DTOs/CompletePatrolDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record CompletePatrolRequest(DateTime CompletedAt);
```

Modify `src/Bekci.Application/Services/PatrolService.cs` — add this method inside the class (after `StartAsync`):
```csharp
    public async Task<PatrolResponse?> CompleteAsync(Guid patrolId, DateTime completedAt, CancellationToken ct)
    {
        var patrol = await db.Patrols.FirstOrDefaultAsync(p => p.Id == patrolId, ct);
        if (patrol is null)
            return null;

        if (patrol.Status != Domain.PatrolStatus.Completed)
        {
            patrol.Complete(DateTime.SpecifyKind(completedAt, DateTimeKind.Utc));
            await db.SaveChangesAsync(ct);
        }

        return Map(patrol);
    }
```

Modify `src/Bekci.Api/Controllers/PatrolsController.cs` — add this action inside the class (after `Scans`):
```csharp
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompletePatrolRequest req, CancellationToken ct)
    {
        var patrol = await service.CompleteAsync(id, req.CompletedAt, ct);
        return patrol is null ? NotFound() : Ok(patrol);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter PatrolCompleteTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Application src/Bekci.Api/Controllers/PatrolsController.cs tests/Bekci.Tests/Integration/PatrolCompleteTests.cs
git commit -m "feat: idempotent patrol completion"
```

---

### Task 15: Supervisor history — list patrols + patrol detail with scans

**Files:**
- Create: `src/Bekci.Application/DTOs/PatrolQueryDtos.cs`, `src/Bekci.Application/Services/PatrolQueryService.cs`
- Create: `src/Bekci.Api/Controllers/SupervisorPatrolsController.cs`
- Modify: `src/Bekci.Application/DependencyInjection.cs` (register `PatrolQueryService`)
- Test: `tests/Bekci.Tests/Integration/SupervisorHistoryTests.cs`

**Interfaces:**
- Consumes: `Repository`, `Patrol`, `Scan`, `Checkpoint`.
- Produces:
  - DTOs `record PatrolSummary(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, int ScanCount)`, `record ScanDetail(Guid Id, Guid CheckpointId, string CheckpointName, DateTime ScannedAt, DateTime ReceivedAt, double? Lat, double? Lng, bool GeoValid, bool OrderValid, bool IsDuplicate)`, `record PatrolDetail(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, List<ScanDetail> Scans)`.
  - `PatrolQueryService.ListAsync(Guid? siteId, Guid? routeId, Guid? guardId, CancellationToken)`, `GetAsync(Guid patrolId, CancellationToken)`.
  - Endpoints `GET /api/v1/patrols` (filters) and `GET /api/v1/patrols/{id}` (role `Supervisor`).

> Note: guard patrol write endpoints live on `PatrolsController` (`[Authorize(Roles="Guard")]`, `POST` only). Supervisor read endpoints live on a separate `SupervisorPatrolsController` with `[Authorize(Roles="Supervisor")]` and the same base route but only `GET` verbs — no conflict since the HTTP methods differ.

- [ ] **Step 1: Write the failing test**

Create `tests/Bekci.Tests/Integration/SupervisorHistoryTests.cs`:
```csharp
using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class SupervisorHistoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt);
    private sealed record ScanInput(Guid ScanId, Guid CheckpointId, DateTime ScannedAt, double? Lat, double? Lng);
    private sealed record IngestScansRequest(List<ScanInput> Scans);
    private sealed record PatrolSummary(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, int ScanCount);
    private sealed record ScanDetail(Guid Id, Guid CheckpointId, string CheckpointName, DateTime ScannedAt, DateTime ReceivedAt, double? Lat, double? Lng, bool GeoValid, bool OrderValid, bool IsDuplicate);
    private sealed record PatrolDetail(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, List<ScanDetail> Scans);

    [Fact]
    public async Task Supervisor_sees_patrol_with_scan_flags()
    {
        var tenantId = Guid.NewGuid();
        var site = Site.Create(tenantId, "Mall A", null);
        var route = Route.Create(tenantId, site.Id, "Loop", enforceOrder: false);
        var cp1 = Checkpoint.Create(tenantId, route.Id, "Lobby", "QR-1", 40.0, 29.0, 25, 1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Repository>();
            db.Database.EnsureCreated();
            db.Sites.Add(site); db.Routes.Add(route); db.Checkpoints.Add(cp1);
            await db.SaveChangesAsync();
        }

        // Guard runs a patrol.
        var (guard, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Guard, tenantId, site.Id);
        var patrolId = Guid.NewGuid();
        await guard.PostAsJsonAsync("/api/v1/patrols", new StartPatrolRequest(patrolId, route.Id, DateTime.UtcNow));
        await guard.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/scans",
            new IngestScansRequest([new ScanInput(Guid.NewGuid(), cp1.Id, DateTime.UtcNow, 40.00005, 29.0)]));

        // Supervisor in the SAME tenant reviews.
        var (sup, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Supervisor, tenantId);
        var list = await sup.GetFromJsonAsync<List<PatrolSummary>>("/api/v1/patrols");
        list.Should().ContainSingle(p => p.Id == patrolId && p.ScanCount == 1);

        var detail = await sup.GetFromJsonAsync<PatrolDetail>($"/api/v1/patrols/{patrolId}");
        detail!.Scans.Should().ContainSingle();
        detail.Scans[0].CheckpointName.Should().Be("Lobby");
        detail.Scans[0].GeoValid.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Bekci.Tests --filter SupervisorHistoryTests`
Expected: FAIL — supervisor patrol endpoints do not exist.

- [ ] **Step 3: Write DTOs, query service, controller, registration**

Create `src/Bekci.Application/DTOs/PatrolQueryDtos.cs`:
```csharp
namespace Bekci.Application.DTOs;

public sealed record PatrolSummary(
    Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, int ScanCount);

public sealed record ScanDetail(
    Guid Id, Guid CheckpointId, string CheckpointName, DateTime ScannedAt, DateTime ReceivedAt,
    double? Lat, double? Lng, bool GeoValid, bool OrderValid, bool IsDuplicate);

public sealed record PatrolDetail(
    Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, List<ScanDetail> Scans);
```

Create `src/Bekci.Application/Services/PatrolQueryService.cs`:
```csharp
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class PatrolQueryService(Repository db)
{
    public async Task<IReadOnlyList<PatrolSummary>> ListAsync(
        Guid? siteId, Guid? routeId, Guid? guardId, CancellationToken ct)
    {
        var query = db.Patrols.AsNoTracking().AsQueryable();

        if (routeId is not null)
            query = query.Where(p => p.RouteId == routeId);
        if (guardId is not null)
            query = query.Where(p => p.GuardId == guardId);
        if (siteId is not null)
            query = query.Where(p => db.Routes.Any(r => r.Id == p.RouteId && r.SiteId == siteId));

        return await query
            .OrderByDescending(p => p.StartedAt)
            .Select(p => new PatrolSummary(
                p.Id, p.RouteId, p.GuardId, p.StartedAt, p.CompletedAt, p.Status.ToString(),
                db.Scans.Count(s => s.PatrolId == p.Id)))
            .ToListAsync(ct);
    }

    public async Task<PatrolDetail?> GetAsync(Guid patrolId, CancellationToken ct)
    {
        var patrol = await db.Patrols.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patrolId, ct);
        if (patrol is null)
            return null;

        var scans = await db.Scans.AsNoTracking()
            .Where(s => s.PatrolId == patrolId)
            .OrderBy(s => s.ScannedAt)
            .Join(db.Checkpoints, s => s.CheckpointId, c => c.Id, (s, c) => new ScanDetail(
                s.Id, s.CheckpointId, c.Name, s.ScannedAt, s.ReceivedAt,
                s.Lat, s.Lng, s.GeoValid, s.OrderValid, s.IsDuplicate))
            .ToListAsync(ct);

        return new PatrolDetail(
            patrol.Id, patrol.RouteId, patrol.GuardId, patrol.StartedAt, patrol.CompletedAt,
            patrol.Status.ToString(), scans);
    }
}
```

Create `src/Bekci.Api/Controllers/SupervisorPatrolsController.cs`:
```csharp
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/patrols")]
[Authorize(Roles = "Supervisor")]
public sealed class SupervisorPatrolsController(PatrolQueryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? siteId, [FromQuery] Guid? routeId, [FromQuery] Guid? guardId, CancellationToken ct) =>
        Ok(await service.ListAsync(siteId, routeId, guardId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var detail = await service.GetAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }
}
```

Modify `src/Bekci.Application/DependencyInjection.cs` — add after `services.AddScoped<ScanService>();`:
```csharp
        services.AddScoped<PatrolQueryService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Bekci.Tests --filter SupervisorHistoryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Bekci.Application src/Bekci.Api/Controllers/SupervisorPatrolsController.cs tests/Bekci.Tests/Integration/SupervisorHistoryTests.cs
git commit -m "feat: supervisor patrol history list + detail with scans"
```

---

### Task 16: End-to-end critical-path test + full suite green

**Files:**
- Create: `tests/Bekci.Tests/Integration/CriticalPathTests.cs`

**Interfaces:**
- Consumes: every endpoint built above. No new production code — this task proves the spec's critical path and guards against regressions.

- [ ] **Step 1: Write the end-to-end test**

Create `tests/Bekci.Tests/Integration/CriticalPathTests.cs`:
```csharp
using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class CriticalPathTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt);
    private sealed record ScanInput(Guid ScanId, Guid CheckpointId, DateTime ScannedAt, double? Lat, double? Lng);
    private sealed record IngestScansRequest(List<ScanInput> Scans);
    private sealed record CompletePatrolRequest(DateTime CompletedAt);
    private sealed record ScanDetail(Guid Id, Guid CheckpointId, string CheckpointName, DateTime ScannedAt, DateTime ReceivedAt, double? Lat, double? Lng, bool GeoValid, bool OrderValid, bool IsDuplicate);
    private sealed record PatrolDetail(Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, List<ScanDetail> Scans);

    [Fact]
    public async Task Full_patrol_loop_online_then_deadzone_flush()
    {
        // Seed ordered route with 3 checkpoints.
        var tenantId = Guid.NewGuid();
        var site = Site.Create(tenantId, "Mall A", null);
        var route = Route.Create(tenantId, site.Id, "Loop", enforceOrder: true);
        var cp1 = Checkpoint.Create(tenantId, route.Id, "Lobby", "QR-1", 40.0000, 29.0000, 25, 1);
        var cp2 = Checkpoint.Create(tenantId, route.Id, "Corridor", "QR-2", 41.0000, 30.0000, 25, 2);
        var cp3 = Checkpoint.Create(tenantId, route.Id, "Roof", "QR-3", 42.0000, 31.0000, 25, 3);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Repository>();
            db.Database.EnsureCreated();
            db.Sites.Add(site); db.Routes.Add(route);
            db.Checkpoints.AddRange(cp1, cp2, cp3);
            await db.SaveChangesAsync();
        }

        var (guard, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Guard, tenantId, site.Id);
        var patrolId = Guid.NewGuid();

        // 1. Start + first scan "online" (cp1, good GPS, in order).
        await guard.PostAsJsonAsync("/api/v1/patrols", new StartPatrolRequest(patrolId, route.Id, DateTime.UtcNow));
        var onlineScan = new ScanInput(Guid.NewGuid(), cp1.Id, DateTime.UtcNow.AddMinutes(-10), 40.00005, 29.0);
        await guard.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/scans", new IngestScansRequest([onlineScan]));

        // 2. Dead zone: cp3 out of order (good GPS) + cp2 no GPS, flushed together.
        var deadA = new ScanInput(Guid.NewGuid(), cp3.Id, DateTime.UtcNow.AddMinutes(-6), 42.00005, 31.0); // out of order
        var deadB = new ScanInput(Guid.NewGuid(), cp2.Id, DateTime.UtcNow.AddMinutes(-4), null, null);      // no gps
        await guard.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/scans", new IngestScansRequest([deadA, deadB]));

        // 3. Re-flush the whole batch (simulates retry) — must stay idempotent.
        await guard.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/scans", new IngestScansRequest([onlineScan, deadA, deadB]));

        // 4. Complete.
        await guard.PostAsJsonAsync($"/api/v1/patrols/{patrolId}/complete", new CompletePatrolRequest(DateTime.UtcNow));

        // Supervisor verifies.
        var (sup, _) = await AuthHelper.LoginAsAsync(factory, UserRole.Supervisor, tenantId);
        var detail = await sup.GetFromJsonAsync<PatrolDetail>($"/api/v1/patrols/{patrolId}");

        detail!.Status.Should().Be("Completed");
        detail.Scans.Should().HaveCount(3); // exactly once each despite re-flush

        var s1 = detail.Scans.Single(s => s.CheckpointId == cp1.Id);
        s1.GeoValid.Should().BeTrue();  s1.OrderValid.Should().BeTrue();

        var s3 = detail.Scans.Single(s => s.CheckpointId == cp3.Id);
        s3.GeoValid.Should().BeTrue();  s3.OrderValid.Should().BeFalse(); // cp3 before cp2

        var s2 = detail.Scans.Single(s => s.CheckpointId == cp2.Id);
        s2.GeoValid.Should().BeFalse(); // no gps
        s2.OrderValid.Should().BeTrue(); // cp2 was next expected after cp1 (cp3 doesn't consume the slot)
    }
}
```

- [ ] **Step 2: Run this test**

Run: `dotnet test tests/Bekci.Tests --filter CriticalPathTests`
Expected: PASS. (If `s2.OrderValid` differs, review the "next expected = first unscanned by sequence" rule in `ScanService`; cp1 is scanned so cp2 is next-expected regardless of cp3.)

- [ ] **Step 3: Run the full suite**

Run: `dotnet test`
Expected: ALL tests PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/Bekci.Tests/Integration/CriticalPathTests.cs
git commit -m "test: end-to-end patrol critical path (online + dead-zone flush + idempotency)"
```

---

## Self-Review

**1. Spec coverage** (against `2026-07-10-guard-tour-phase-1-design.md`):

| Spec item | Task |
|---|---|
| Multi-tenant auth, roles | Tasks 2, 6, 7 |
| Organization/Site/Route/Checkpoint model | Tasks 1, 3, 6 |
| Patrol/Scan model incl. client GUIDs, ReceivedAt, IsDuplicate | Tasks 4, 5, 6 |
| Supervisor creates site/route/checkpoints | Tasks 9, 10, 11 |
| Guard lists routes at their site, starts patrol | Task 12 |
| Scan ingestion: one-or-many, idempotent, server re-validation | Task 13 |
| Geofence soft flag, order validation, EnforceOrder toggle | Tasks 5, 13 |
| Complete patrol | Task 14 |
| Supervisor history: list + detail with scans | Task 15 |
| Tenant isolation enforced by query filter | Tasks 6, 9 (isolation test) |
| Critical path (online → dead zone → flush once → verdicts) | Task 16 |
| xUnit + Testcontainers | Tasks 7, 16 |
| Guard self-selects route (Phase 1 stand-in) | Task 12 |

Deferred items (shifts, SignalR, panic, photos, time windows) are intentionally absent — matches the spec's "Out of Scope" section.

**2. Placeholder scan:** No "TBD/TODO/handle edge cases" in production steps. Every code step shows complete code; every test step shows complete test code.

**3. Type consistency:** `ITenantContext` (`TenantId`, `HasTenant`) is used identically in Tasks 6, 7, 9–13. `Patrol.Start(id, tenantId, routeId, guardId, startedAt)` and `Scan.Record(...)` signatures match their call sites in Tasks 12–13. `Repository` DbSet names (`Sites`, `Routes`, `Checkpoints`, `Patrols`, `Scans`) are consistent across all services. DTO record shapes are duplicated verbatim in the integration tests that consume them (tests declare local mirrors to avoid coupling to the Application assembly's DTOs — intentional and consistent).

One resolved risk: `PatrolsController` (Guard, POST-only) and `SupervisorPatrolsController` (Supervisor, GET-only) share the `api/v1/patrols` route but differ by HTTP verb and role — no routing conflict (noted in Task 15).

---

## Notes for the implementer

- **Docker must be running** for the integration tests (Testcontainers spins up Postgres). Domain unit tests (Tasks 1–5) need no Docker.
- Package versions pinned to `10.0.0` are best-effort; if restore fails, take the newest `10.0.*` (`dotnet package search <pkg> --prerelease`) and keep all EF/ASP.NET packages on the same minor.
- After any change to a domain entity or EF configuration, **regenerate the migration** (`dotnet ef migrations add <Name> ...`); the `MigrationTests` guard will fail if pending model changes exist.
