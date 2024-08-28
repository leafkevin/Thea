using Microsoft.Extensions.DependencyInjection;

namespace Thea.Cache;

public static class TheaCacheExtensions
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services)
    {
        services.AddSingleton<IDistributedCache, RedisCache>();
        return services;
    }
}
