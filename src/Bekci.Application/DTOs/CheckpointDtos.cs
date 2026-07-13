namespace Bekci.Application.DTOs;

public sealed record CreateCheckpointRequest(
    string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence);

public sealed record CheckpointResponse(
    Guid Id, Guid RouteId, string Name, string QrCode, double Lat, double Lng, double GeofenceRadiusM, int Sequence);
