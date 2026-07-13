namespace Bekci.Application.DTOs;

public sealed record PatrolSummary(
    Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, int ScanCount);

public sealed record ScanDetail(
    Guid Id, Guid CheckpointId, string CheckpointName, DateTime ScannedAt, DateTime ReceivedAt,
    double? Lat, double? Lng, bool GeoValid, bool OrderValid, bool IsDuplicate);

public sealed record PatrolDetail(
    Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status, List<ScanDetail> Scans);
