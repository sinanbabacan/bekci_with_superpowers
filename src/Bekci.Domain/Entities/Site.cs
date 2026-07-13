namespace Bekci.Domain.Entities;

public sealed class Site : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Address { get; private set; }

    private Site() { }

    public static Site Create(Guid tenantId, string name, string? address) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Address = address
    };
}
