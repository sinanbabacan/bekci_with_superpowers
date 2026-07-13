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
        s3.GeoValid.Should().BeTrue();  s3.OrderValid.Should().BeFalse(); // cp3 skipped ahead over cp2

        var s2 = detail.Scans.Single(s => s.CheckpointId == cp2.Id);
        s2.GeoValid.Should().BeFalse(); // no gps
        s2.OrderValid.Should().BeFalse(); // cp2 scanned after reaching cp3 → backtrack → out of order under strict monotonic Rule B
    }
}
