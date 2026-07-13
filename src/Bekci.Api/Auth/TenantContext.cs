using System.Security.Claims;
using Bekci.Application.Abstractions;

namespace Bekci.Api.Auth;

public sealed class TenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var claim = accessor.HttpContext?.User.FindFirstValue("tenant_id");
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }

    public bool HasTenant => TenantId != Guid.Empty;
}
