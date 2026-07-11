using Microsoft.Extensions.DependencyInjection;

namespace Bekci.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application services are registered in later tasks.
        return services;
    }
}
