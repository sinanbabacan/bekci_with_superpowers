namespace Bekci.Application.DTOs;

public sealed record StartPatrolRequest(Guid PatrolId, Guid RouteId, DateTime StartedAt);
public sealed record PatrolResponse(
    Guid Id, Guid RouteId, Guid GuardId, DateTime StartedAt, DateTime? CompletedAt, string Status);
