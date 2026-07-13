namespace Bekci.Application.DTOs;

public sealed record CreateRouteRequest(Guid SiteId, string Name, bool EnforceOrder);
public sealed record RouteResponse(Guid Id, Guid SiteId, string Name, bool EnforceOrder);
