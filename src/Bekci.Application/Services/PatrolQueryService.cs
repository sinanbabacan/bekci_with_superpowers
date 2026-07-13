using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class PatrolQueryService(Repository db)
{
    public async Task<IReadOnlyList<PatrolSummary>> ListAsync(
        Guid? siteId, Guid? routeId, Guid? guardId, CancellationToken ct)
    {
        var query = db.Patrols.AsNoTracking().AsQueryable();

        if (routeId is not null)
            query = query.Where(p => p.RouteId == routeId);
        if (guardId is not null)
            query = query.Where(p => p.GuardId == guardId);
        if (siteId is not null)
            query = query.Where(p => db.Routes.Any(r => r.Id == p.RouteId && r.SiteId == siteId));

        return await query
            .OrderByDescending(p => p.StartedAt)
            .Select(p => new PatrolSummary(
                p.Id, p.RouteId, p.GuardId, p.StartedAt, p.CompletedAt, p.Status.ToString(),
                db.Scans.Count(s => s.PatrolId == p.Id)))
            .ToListAsync(ct);
    }

    public async Task<PatrolDetail?> GetAsync(Guid patrolId, CancellationToken ct)
    {
        var patrol = await db.Patrols.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patrolId, ct);
        if (patrol is null)
            return null;

        var scans = await db.Scans.AsNoTracking()
            .Where(s => s.PatrolId == patrolId)
            .OrderBy(s => s.ScannedAt)
            .Join(db.Checkpoints, s => s.CheckpointId, c => c.Id, (s, c) => new ScanDetail(
                s.Id, s.CheckpointId, c.Name, s.ScannedAt, s.ReceivedAt,
                s.Lat, s.Lng, s.GeoValid, s.OrderValid, s.IsDuplicate))
            .ToListAsync(ct);

        return new PatrolDetail(
            patrol.Id, patrol.RouteId, patrol.GuardId, patrol.StartedAt, patrol.CompletedAt,
            patrol.Status.ToString(), scans);
    }
}
