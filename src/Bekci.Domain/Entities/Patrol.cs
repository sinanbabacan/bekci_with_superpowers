namespace Bekci.Domain.Entities;

public sealed class Patrol : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid GuardId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public PatrolStatus Status { get; private set; }

    private Patrol() { }

    public static Patrol Start(Guid id, Guid tenantId, Guid routeId, Guid guardId, DateTime startedAt) => new()
    {
        Id = id,
        TenantId = tenantId,
        RouteId = routeId,
        GuardId = guardId,
        StartedAt = startedAt,
        Status = PatrolStatus.InProgress
    };

    public void Complete(DateTime completedAt)
    {
        Status = PatrolStatus.Completed;
        CompletedAt = completedAt;
    }
}
