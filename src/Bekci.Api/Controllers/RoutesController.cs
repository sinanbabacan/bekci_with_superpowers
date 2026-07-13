using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/routes")]
[Authorize(Roles = "Supervisor")]
public sealed class RoutesController(RouteService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRouteRequest req, CancellationToken ct)
    {
        var route = await service.CreateAsync(req, ct);
        return CreatedAtAction(nameof(ListBySite), new { siteId = route.SiteId }, route);
    }

    [HttpGet]
    public async Task<IActionResult> ListBySite([FromQuery] Guid siteId, CancellationToken ct) =>
        Ok(await service.ListBySiteAsync(siteId, ct));
}
