using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/patrols")]
[Authorize(Roles = "Supervisor")]
public sealed class SupervisorPatrolsController(PatrolQueryService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? siteId, [FromQuery] Guid? routeId, [FromQuery] Guid? guardId, CancellationToken ct) =>
        Ok(await service.ListAsync(siteId, routeId, guardId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var detail = await service.GetAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }
}
