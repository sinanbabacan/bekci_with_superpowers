namespace Bekci.Domain.Entities;

public sealed class Checkpoint : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid RouteId { get; private set; }
    public string Name { get; private set; } = default!;
    public string QrCode { get; private set; } = default!;
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public double GeofenceRadiusM { get; private set; }
    public int Sequence { get; private set; }

    private Checkpoint() { }

    public static Checkpoint Create(
        Guid tenantId, Guid routeId, string name, string qrCode,
        double lat, double lng, double geofenceRadiusM, int sequence) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        RouteId = routeId,
        Name = name,
        QrCode = qrCode,
        Lat = lat,
        Lng = lng,
        GeofenceRadiusM = geofenceRadiusM,
        Sequence = sequence
    };
}
