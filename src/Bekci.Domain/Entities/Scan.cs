namespace Bekci.Domain.Entities;

public sealed class Scan : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid PatrolId { get; private set; }
    public Guid CheckpointId { get; private set; }
    public DateTime ScannedAt { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public double? Lat { get; private set; }
    public double? Lng { get; private set; }
    public bool GeoValid { get; private set; }
    public bool OrderValid { get; private set; }
    public bool IsDuplicate { get; private set; }

    private Scan() { }

    public static Scan Record(
        Guid id, Guid tenantId, Guid patrolId, Guid checkpointId,
        DateTime scannedAt, DateTime receivedAt, double? lat, double? lng,
        bool geoValid, bool orderValid, bool isDuplicate) => new()
    {
        Id = id,
        TenantId = tenantId,
        PatrolId = patrolId,
        CheckpointId = checkpointId,
        ScannedAt = scannedAt,
        ReceivedAt = receivedAt,
        Lat = lat,
        Lng = lng,
        GeoValid = geoValid,
        OrderValid = orderValid,
        IsDuplicate = isDuplicate
    };
}
