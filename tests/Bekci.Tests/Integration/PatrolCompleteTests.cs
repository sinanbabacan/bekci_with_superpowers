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
