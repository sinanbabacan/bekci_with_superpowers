namespace Bekci.Application.DTOs;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, string Role, Guid TenantId);
