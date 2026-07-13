using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
