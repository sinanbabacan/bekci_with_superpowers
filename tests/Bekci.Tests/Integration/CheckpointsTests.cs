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
