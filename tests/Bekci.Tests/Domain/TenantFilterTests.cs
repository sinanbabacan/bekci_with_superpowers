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

    [Fact]
    public async Task Query_isolates_all_six_tenant_scoped_entities()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<Repository>()
            .UseInMemoryDatabase($"tenant-{Guid.NewGuid()}")
            .Options;

        // Common IDs for entity relationships
        var siteAId = Guid.NewGuid();
        var siteBId = Guid.NewGuid();
        var routeAId = Guid.NewGuid();
        var routeBId = Guid.NewGuid();
        var checkpointAId = Guid.NewGuid();
        var checkpointBId = Guid.NewGuid();
        var patrolAId = Guid.NewGuid();
        var patrolBId = Guid.NewGuid();
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var guardAId = Guid.NewGuid();
        var guardBId = Guid.NewGuid();

        // Seed all six entities for both tenants under tenant A context
        await using (var db = new Repository(options, new FixedTenant(tenantA)))
        {
            // Sites
            db.Sites.Add(Site.Create(tenantA, "Site-A", "Address A"));
            db.Sites.Add(Site.Create(tenantB, "Site-B", "Address B"));

            // Routes
            db.Routes.Add(Route.Create(tenantA, siteAId, "Route-A", enforceOrder: true));
            db.Routes.Add(Route.Create(tenantB, siteBId, "Route-B", enforceOrder: false));

            // Checkpoints
            db.Checkpoints.Add(Checkpoint.Create(tenantA, routeAId, "Checkpoint-A", "QR-A", 40.7, -74.0, 50.0, 1));
            db.Checkpoints.Add(Checkpoint.Create(tenantB, routeBId, "Checkpoint-B", "QR-B", 34.0, -118.0, 75.0, 1));

            // Users
            db.Users.Add(User.Create(tenantA, "user-a@test.com", "hash-a", UserRole.Guard, userAId));
            db.Users.Add(User.Create(tenantB, "user-b@test.com", "hash-b", UserRole.Supervisor, userBId));

            // Patrols
            db.Patrols.Add(Patrol.Start(patrolAId, tenantA, routeAId, guardAId, DateTime.UtcNow));
            db.Patrols.Add(Patrol.Start(patrolBId, tenantB, routeBId, guardBId, DateTime.UtcNow));

            // Scans
            db.Scans.Add(Scan.Record(Guid.NewGuid(), tenantA, patrolAId, checkpointAId, DateTime.UtcNow, DateTime.UtcNow, 40.7, -74.0, true, true, false));
            db.Scans.Add(Scan.Record(Guid.NewGuid(), tenantB, patrolBId, checkpointBId, DateTime.UtcNow, DateTime.UtcNow, 34.0, -118.0, true, true, false));

            await db.SaveChangesAsync();
        }

        // Read all entities under tenant B context - should see only tenant B's rows
        await using (var db = new Repository(options, new FixedTenant(tenantB)))
        {
            var sites = await db.Sites.ToListAsync();
            sites.Should().ContainSingle();
            sites[0].Name.Should().Be("Site-B");

            var routes = await db.Routes.ToListAsync();
            routes.Should().ContainSingle();
            routes[0].Name.Should().Be("Route-B");

            var checkpoints = await db.Checkpoints.ToListAsync();
            checkpoints.Should().ContainSingle();
            checkpoints[0].Name.Should().Be("Checkpoint-B");

            var users = await db.Users.ToListAsync();
            users.Should().ContainSingle();
            users[0].Email.Should().Be("user-b@test.com");

            var patrols = await db.Patrols.ToListAsync();
            patrols.Should().ContainSingle();
            patrols[0].Id.Should().Be(patrolBId);

            var scans = await db.Scans.ToListAsync();
            scans.Should().ContainSingle();
            scans[0].Lat.Should().Be(34.0);
        }
    }
}
