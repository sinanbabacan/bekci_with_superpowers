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
