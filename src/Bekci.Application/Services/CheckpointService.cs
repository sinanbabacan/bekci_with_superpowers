using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class CheckpointService(Repository db, ITenantContext tenant)
{
    public async Task<CheckpointResponse> AddAsync(Guid routeId, CreateCheckpointRequest req, CancellationToken ct)
    {
        var cp = Checkpoint.Create(tenant.TenantId, routeId, req.Name, req.QrCode,
            req.Lat, req.Lng, req.GeofenceRadiusM, req.Sequence);
        db.Checkpoints.Add(cp);
        await db.SaveChangesAsync(ct);
        return Map(cp);
    }

    public async Task<IReadOnlyList<CheckpointResponse>> ListByRouteAsync(Guid routeId, CancellationToken ct) =>
        await db.Checkpoints
            .AsNoTracking()
            .Where(c => c.RouteId == routeId)
            .OrderBy(c => c.Sequence)
            .Select(c => new CheckpointResponse(c.Id, c.RouteId, c.Name, c.QrCode, c.Lat, c.Lng, c.GeofenceRadiusM, c.Sequence))
            .ToListAsync(ct);

    private static CheckpointResponse Map(Checkpoint c) =>
        new(c.Id, c.RouteId, c.Name, c.QrCode, c.Lat, c.Lng, c.GeofenceRadiusM, c.Sequence);
}
