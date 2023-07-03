using Microsoft.Extensions.DependencyInjection;

namespace Thea.Globalization;

public static class TheaGlobalizationExtensions
{
    public static IServiceCollection AddGlobalization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IGlobalizationResource, GlobalizationResource>();
        return services;
    }
}
