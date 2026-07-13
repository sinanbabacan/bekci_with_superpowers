using Bekci.Application.DTOs;
using Bekci.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bekci.Api.Controllers;

[ApiController]
[Route("api/v1/sites")]
[Authorize(Roles = "Supervisor")]
public sealed class SitesController(SiteService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSiteRequest req, CancellationToken ct)
    {
        var site = await service.CreateAsync(req, ct);
        return CreatedAtAction(nameof(List), new { }, site);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(ct));
}
