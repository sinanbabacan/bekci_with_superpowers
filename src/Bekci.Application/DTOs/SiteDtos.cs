namespace Bekci.Application.DTOs;

public sealed record CreateSiteRequest(string Name, string? Address);
public sealed record SiteResponse(Guid Id, string Name, string? Address);
