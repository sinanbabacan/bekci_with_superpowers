namespace Bekci.Domain.Entities;

public sealed class User : Entity, ITenantEntity
{
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public Guid? SiteId { get; private set; }

    private User() { }

    public static User Create(Guid tenantId, string email, string passwordHash, UserRole role, Guid? siteId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email,
        PasswordHash = passwordHash,
        Role = role,
        SiteId = siteId
    };
}
