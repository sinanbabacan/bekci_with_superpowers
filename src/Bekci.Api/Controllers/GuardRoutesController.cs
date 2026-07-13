using Bekci.Api.Auth;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/guard/routes")]
[Authorize(Roles = "Guard")]
public sealed class GuardRoutesController(PatrolService service, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await service.ListRoutesForGuardAsync(currentUser.SiteId, ct));
}
