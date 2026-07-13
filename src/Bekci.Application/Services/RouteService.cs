using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class RouteService(Repository db, ITenantContext tenant)
{
    public async Task<RouteResponse> CreateAsync(CreateRouteRequest req, CancellationToken ct)
    {
        var route = Route.Create(tenant.TenantId, req.SiteId, req.Name, req.EnforceOrder);
        db.Routes.Add(route);
        await db.SaveChangesAsync(ct);
        return new RouteResponse(route.Id, route.SiteId, route.Name, route.EnforceOrder);
    }

    public async Task<IReadOnlyList<RouteResponse>> ListBySiteAsync(Guid siteId, CancellationToken ct) =>
        await db.Routes
            .AsNoTracking()
            .Where(r => r.SiteId == siteId)
            .OrderBy(r => r.Name)
            .Select(r => new RouteResponse(r.Id, r.SiteId, r.Name, r.EnforceOrder))
            .ToListAsync(ct);
}
