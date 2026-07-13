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

        // Bounded retry: on a PK conflict, only the scans whose client-supplied ScanId raced with a
        // concurrent insert actually fail - the rest of the batch is lost too, because SaveChangesAsync
        // is one transaction and a single PK violation rolls the whole thing back. Rather than surfacing
        // that as a hard failure (which would also drop the genuinely-new, non-conflicting scans from
        // this attempt), we detach our failed adds and re-run the compute+add pass once more. On the
        // retry, the previously-conflicting ScanIds are now visible in `existing` (freshly re-queried),
        // so the idempotent "already stored" check above skips them and only the still-new scans get
        // added - that second SaveChangesAsync succeeds. Capped at 2 attempts total so a non-conflict
        // related DbUpdateException (e.g. a genuine DB outage) still surfaces instead of looping.
        const int maxAttempts = 2;
        List<ScanResult> results = [];

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Existing scans for this patrol (source of truth for dedupe + ordering). Re-queried fresh
            // on every attempt so a retry sees whatever the previous attempt's conflicting winners wrote.
            var existing = await db.Scans.AsNoTracking().Where(x => x.PatrolId == patrolId).ToListAsync(ct);
            results = [];
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
                break; // success - stop retrying
            }
            catch (DbUpdateException)
            {
                // Lost a race: a concurrent request already persisted one or more of the client-supplied
                // ScanIds in this batch (Scan.Id is the PK), so this insert violated the PK/unique
                // constraint. Detach every entity we just tried to add - they're still tracked by the
                // change tracker despite the failed save, so a subsequent tracked query would just hand
                // back these same broken instances (or throw on identity-map conflicts once the retry
                // re-adds a fresh instance with the same key).
                foreach (var added in newlyAdded)
                    db.Entry(added).State = EntityState.Detached;

                if (attempt >= maxAttempts)
                    // Exhausted retries - this isn't converging via the idempotent-skip path (e.g. a
                    // genuine DB outage), so surface the failure instead of looping forever.
                    throw;

                // Otherwise fall through to the next attempt: `existing` will be re-queried fresh above,
                // which now includes whatever the racing request(s) committed, so the idempotent
                // "already stored" check will skip those ScanIds and only genuinely-new scans get added.
            }
        }

        return new IngestScansResponse(results);
    }
}
