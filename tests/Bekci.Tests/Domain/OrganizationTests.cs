using Bekci.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Bekci.Tests.Domain;

public class OrganizationTests
{
    [Fact]
    public void Create_sets_name_and_generates_id()
    {
        var org = Organization.Create("Acme Security");

        org.Name.Should().Be("Acme Security");
        org.Id.Should().NotBe(Guid.Empty);
    }
}
