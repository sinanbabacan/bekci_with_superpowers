using System.Net;
using System.Net.Http.Json;
using Bekci.Application.Abstractions;
using Bekci.Application.Data;
using Bekci.Domain.Entities;
using Bekci.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bekci.Tests.Integration;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record LoginRequest(string Email, string Password);
    private sealed record LoginResponse(string Token, string Role, Guid TenantId);

    [Fact]
    public async Task Login_with_valid_credentials_returns_token()
    {
        var tenantId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Repository>();
            db.Database.EnsureCreated();
            db.Users.Add(User.Create(tenantId, "sup@acme.com",
                BCrypt.Net.BCrypt.HashPassword("pass123"), UserRole.Supervisor, null));
            db.SaveChanges();
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("sup@acme.com", "pass123"));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Token.Should().NotBeNullOrEmpty();
        body.Role.Should().Be("Supervisor");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("sup@acme.com", "wrong"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
