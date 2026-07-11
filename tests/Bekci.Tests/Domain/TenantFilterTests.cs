using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bekci.Tests.Domain;

public class TenantFilterTests
{
    private sealed class FixedTenant(Guid id) : ITenantContext
    {
        public Guid TenantId => id;
        public bool HasTenant => id != Guid.Empty;
    }

    [Fact]
    public async Task Query_only_returns_current_tenant_rows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<Repository>()
            .UseInMemoryDatabase($"tenant-{Guid.NewGuid()}")
            .Options;

        // Seed with tenant A context
        await using (var db = new Repository(options, new FixedTenant(tenantA)))
        {
            db.Sites.Add(Site.Create(tenantA, "A-Site", null));
            db.Sites.Add(Site.Create(tenantB, "B-Site", null));
            await db.SaveChangesAsync();
        }

        // Read with tenant B context
        await using (var db = new Repository(options, new FixedTenant(tenantB)))
        {
            var sites = await db.Sites.ToListAsync();
            sites.Should().ContainSingle();
            sites[0].Name.Should().Be("B-Site");
        }
    }
}
