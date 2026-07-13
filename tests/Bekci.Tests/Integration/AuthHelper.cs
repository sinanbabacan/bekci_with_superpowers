using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bekci.Application.Data;
using Bekci.Domain;
using Bekci.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Bekci.Tests.Integration;

public static class AuthHelper
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record LoginResponse(string Token, string Role, Guid TenantId);

    /// Seeds a user of the given role and returns an HttpClient with the bearer token attached.
    public static async Task<(HttpClient Client, Guid TenantId)> LoginAsAsync(
        ApiFactory factory, UserRole role, Guid tenantId, Guid? siteId = null, string? email = null)
    {
        email ??= $"{role}-{Guid.NewGuid():N}@acme.com";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Repository>();
            db.Database.EnsureCreated();
            db.Users.Add(User.Create(tenantId, email,
                BCrypt.Net.BCrypt.HashPassword("pass123"), role, siteId));
            db.SaveChanges();
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "pass123"));
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return (client, tenantId);
    }
}
