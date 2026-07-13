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
