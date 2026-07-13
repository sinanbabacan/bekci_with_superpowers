using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Bekci.Api.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }
    Guid TenantId { get; }
    string Role { get; }
    Guid? SiteId { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid UserId => Guid.TryParse(User?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;
    public Guid TenantId => Guid.TryParse(User?.FindFirstValue("tenant_id"), out var id) ? id : Guid.Empty;
    public string Role => User?.FindFirstValue(ClaimTypes.Role) ?? "";
    public Guid? SiteId => Guid.TryParse(User?.FindFirstValue("site_id"), out var id) ? id : null;
}
