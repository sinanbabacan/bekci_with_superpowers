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
