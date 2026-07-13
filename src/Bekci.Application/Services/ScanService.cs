using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Application.DTOs;
using Bekci.Domain;
using Bekci.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bekci.Application.Services;

public sealed class ScanService(Repository db, ITenantContext tenant)
{
    public async Task<IngestScansResponse> IngestAsync(Guid patrolId, IngestScansRequest req, CancellationToken ct)
    {
        var patrol = await db.Patrols.FirstOrDefaultAsync(p => p.Id == patrolId, ct)
            ?? throw new InvalidOperationException("Patrol not found.");

        var route = await db.Routes.FirstAsync(r => r.Id == patrol.RouteId, ct);
        var checkpoints = await db.Checkpoints
            .Where(c => c.RouteId == route.Id)
            .OrderBy(c => c.Sequence)
            .ToListAsync(ct);

        // Existing scans for this patrol (source of truth for dedupe + ordering).
        var existing = await db.Scans.Where(x => x.PatrolId == patrolId).ToListAsync(ct);
        var results = new List<ScanResult>();
        var newlyAdded = new List<Scan>();

        // Process inputs in ScannedAt order so ordering verdicts are deterministic.
        foreach (var input in req.Scans.OrderBy(x => x.ScannedAt))
        {
            var already = existing.FirstOrDefault(x => x.Id == input.ScanId);
            if (already is not null)
            {
                results.Add(new ScanResult(already.Id, already.GeoValid, already.OrderValid, already.IsDuplicate));
                continue;
            }

            var cp = checkpoints.FirstOrDefault(c => c.Id == input.CheckpointId);
            if (cp is null)
                throw new InvalidOperationException("Checkpoint is not part of this patrol's route.");

            var geoValid = ScanValidation.IsWithinGeofence(cp.Lat, cp.Lng, cp.GeofenceRadiusM, input.Lat, input.Lng);

            var alreadyScannedThisCp = existing.Any(x => x.CheckpointId == cp.Id);
            var isDuplicate = alreadyScannedThisCp;

            bool orderValid;
            if (!route.EnforceOrder)
            {
                orderValid = true;
            }
            else
            {
                // Next expected = first checkpoint by sequence that comes after the patrol's
                // furthest progress so far (the highest sequence number scanned to date) and is
                // not itself already scanned. Using "furthest progress" rather than "any unscanned
                // checkpoint" means that once the guard scans ahead (e.g. cp2 before cp1), earlier,
                // skipped checkpoints (cp1) are no longer considered "next expected" - scanning them
                // later is out of order too, not just the checkpoint that jumped ahead.
                var scannedCheckpointIds = existing.Select(x => x.CheckpointId).ToHashSet();
                var maxScannedSequence = checkpoints
                    .Where(c => scannedCheckpointIds.Contains(c.Id))
                    .Select(c => (int?)c.Sequence)
                    .Max() ?? 0;
                var nextExpected = checkpoints.FirstOrDefault(c =>
                    c.Sequence > maxScannedSequence && !scannedCheckpointIds.Contains(c.Id));
                orderValid = nextExpected is not null && nextExpected.Id == cp.Id;
            }

            var scan = Scan.Record(
                input.ScanId, tenant.TenantId, patrolId, cp.Id,
                DateTime.SpecifyKind(input.ScannedAt, DateTimeKind.Utc),
                DateTime.UtcNow, input.Lat, input.Lng, geoValid, orderValid, isDuplicate);

            db.Scans.Add(scan);
            newlyAdded.Add(scan);
            existing.Add(scan); // affects ordering/dedupe of subsequent inputs in the same batch
            results.Add(new ScanResult(scan.Id, geoValid, orderValid, isDuplicate));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race: a concurrent request already persisted one or more of the client-supplied
            // ScanIds in this batch (Scan.Id is the PK), so this insert violated the PK/unique
            // constraint. Recover instead of surfacing the exception - detach every entity we just
            // tried to add (they're still tracked by the change tracker despite the failed save, so a
            // tracked re-query would just hand back these same broken instances), then re-query with
            // AsNoTracking() to fetch whatever the winning request actually persisted and swap those
            // verdicts into the results in place of our locally-computed ones.
            foreach (var added in newlyAdded)
                db.Entry(added).State = EntityState.Detached;

            var addedIds = newlyAdded.Select(x => x.Id).ToHashSet();
            var winners = await db.Scans.AsNoTracking()
                .Where(x => addedIds.Contains(x.Id))
                .ToListAsync(ct);

            if (winners.Count != addedIds.Count)
                // SaveChangesAsync is one transaction: a PK conflict on any single scan rolls back
                // the whole batch, so any of our newlyAdded scans that DIDN'T conflict are also not
                // persisted and won't show up here. Rather than silently reporting success for scans
                // that were never written, surface the failure so the client resubmits the batch -
                // the retry is safe because every scan in it is idempotent on its own ScanId.
                throw;

            for (var i = 0; i < results.Count; i++)
            {
                var winner = winners.FirstOrDefault(w => w.Id == results[i].ScanId);
                if (winner is not null)
                    results[i] = new ScanResult(winner.Id, winner.GeoValid, winner.OrderValid, winner.IsDuplicate);
            }
        }

        return new IngestScansResponse(results);
    }
}
