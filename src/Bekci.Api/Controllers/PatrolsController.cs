using Bekci.Api.Auth;
using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/patrols")]
[Authorize(Roles = "Guard")]
public sealed class PatrolsController(
    PatrolService service, ScanService scanService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartPatrolRequest req, CancellationToken ct)
    {
        var patrol = await service.StartAsync(req, currentUser.UserId, ct);
        return CreatedAtAction(nameof(Start), new { id = patrol.Id }, patrol);
    }

    [HttpPost("{id:guid}/scans")]
    public async Task<IActionResult> Scans(Guid id, [FromBody] IngestScansRequest req, CancellationToken ct)
    {
        var result = await scanService.IngestAsync(id, req, ct);
        return Ok(result);
    }
}
