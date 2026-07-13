using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class SiteService(Repository db, ITenantContext tenant)
{
    public async Task<SiteResponse> CreateAsync(CreateSiteRequest req, CancellationToken ct)
    {
        var site = Site.Create(tenant.TenantId, req.Name, req.Address);
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return new SiteResponse(site.Id, site.Name, site.Address);
    }

    public async Task<IReadOnlyList<SiteResponse>> ListAsync(CancellationToken ct) =>
        await db.Sites
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SiteResponse(s.Id, s.Name, s.Address))
            .ToListAsync(ct);
}
