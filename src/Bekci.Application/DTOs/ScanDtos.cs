namespace Bekci.Application.DTOs;

public sealed record ScanInput(Guid ScanId, Guid CheckpointId, DateTime ScannedAt, double? Lat, double? Lng);
public sealed record IngestScansRequest(List<ScanInput> Scans);
public sealed record ScanResult(Guid ScanId, bool GeoValid, bool OrderValid, bool IsDuplicate);
public sealed record IngestScansResponse(List<ScanResult> Results);
