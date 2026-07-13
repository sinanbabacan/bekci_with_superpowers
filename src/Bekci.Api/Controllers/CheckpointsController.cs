using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/routes/{routeId:guid}/checkpoints")]
[Authorize(Roles = "Supervisor")]
public sealed class CheckpointsController(CheckpointService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add(Guid routeId, [FromBody] CreateCheckpointRequest req, CancellationToken ct)
    {
        var cp = await service.AddAsync(routeId, req, ct);
        return CreatedAtAction(nameof(List), new { routeId }, cp);
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid routeId, CancellationToken ct) =>
        Ok(await service.ListByRouteAsync(routeId, ct));
}
