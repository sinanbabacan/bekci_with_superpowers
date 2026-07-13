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
