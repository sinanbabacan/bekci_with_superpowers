using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class PatrolService(Repository db, ITenantContext tenant)
{
    /// The current guard's site id is passed in from the controller (claim-sourced).
    public async Task<IReadOnlyList<RouteResponse>> ListRoutesForGuardAsync(Guid? guardSiteId, CancellationToken ct)
    {
        if (guardSiteId is null)
            return [];

        return await db.Routes
            .AsNoTracking()
            .Where(r => r.SiteId == guardSiteId)
            .OrderBy(r => r.Name)
            .Select(r => new RouteResponse(r.Id, r.SiteId, r.Name, r.EnforceOrder))
            .ToListAsync(ct);
    }

    public async Task<PatrolResponse> StartAsync(StartPatrolRequest req, Guid guardId, CancellationToken ct)
    {
        var existing = await db.Patrols.FirstOrDefaultAsync(p => p.Id == req.PatrolId, ct);
        if (existing is not null)
            return Map(existing); // idempotent

        var patrol = Patrol.Start(req.PatrolId, tenant.TenantId, req.RouteId, guardId,
            DateTime.SpecifyKind(req.StartedAt, DateTimeKind.Utc));
        db.Patrols.Add(patrol);
        await db.SaveChangesAsync(ct);
        return Map(patrol);
    }

    private static PatrolResponse Map(Patrol p) =>
        new(p.Id, p.RouteId, p.GuardId, p.StartedAt, p.CompletedAt, p.Status.ToString());
}
