namespace Bekci.Domain.Entities;

public sealed class Route : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public Guid SiteId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool EnforceOrder { get; private set; }

    private Route() { }

    public static Route Create(Guid tenantId, Guid siteId, string name, bool enforceOrder) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        SiteId = siteId,
        Name = name,
        EnforceOrder = enforceOrder
    };
}
