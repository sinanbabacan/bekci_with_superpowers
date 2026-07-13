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
