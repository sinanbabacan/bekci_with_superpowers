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
        try
        {
            await db.SaveChangesAsync(ct);
            return Map(patrol);
        }
        catch (DbUpdateException)
        {
            // Lost a race: another concurrent request with the same client-supplied PatrolId
            // committed first, so this insert violated the PK/unique constraint. Treat this as
            // the idempotent case rather than surfacing the exception - detach the failed entity
            // (it's still tracked by the change tracker despite the failed save, so a tracked
            // re-query would just hand back this same broken instance) and re-query with
            // AsNoTracking() to fetch the row the winning request actually persisted.
            db.Entry(patrol).State = EntityState.Detached;
            var winner = await db.Patrols.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PatrolId, ct);
            if (winner is null)
                throw;
            return Map(winner);
        }
    }

    private static PatrolResponse Map(Patrol p) =>
        new(p.Id, p.RouteId, p.GuardId, p.StartedAt, p.CompletedAt, p.Status.ToString());
}
