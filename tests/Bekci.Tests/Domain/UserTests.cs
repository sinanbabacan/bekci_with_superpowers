using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class UserTests
{
    [Fact]
    public void Create_guard_sets_all_fields()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var user = User.Create(tenantId, "guard@acme.com", "hash", UserRole.Guard, siteId);

        user.TenantId.Should().Be(tenantId);
        user.Email.Should().Be("guard@acme.com");
        user.PasswordHash.Should().Be("hash");
        user.Role.Should().Be(UserRole.Guard);
        user.SiteId.Should().Be(siteId);
        user.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_supervisor_has_no_site()
    {
        var user = User.Create(Guid.NewGuid(), "sup@acme.com", "hash", UserRole.Supervisor, null);

        user.SiteId.Should().BeNull();
    }
}
