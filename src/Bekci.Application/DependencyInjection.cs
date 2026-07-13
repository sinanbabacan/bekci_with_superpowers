using Bekci.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bekci.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SiteService>();
        services.AddScoped<RouteService>();
        services.AddScoped<CheckpointService>();
        services.AddScoped<PatrolService>();
        services.AddScoped<ScanService>();
        services.AddScoped<PatrolQueryService>();
        return services;
    }
}
