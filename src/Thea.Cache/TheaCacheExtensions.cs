using Microsoft.Extensions.DependencyInjection;
using System;

namespace Thea.Cache;

public static class TheaCacheExtensions
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services, Func<string, int> databaseSelector = null)
    {
        services.AddSingleton<IDistributedCache>(f =>
        {
            var redisCache = new RedisCache(f);
            if (databaseSelector != null)
                redisCache.UserDatabase(databaseSelector);
            return redisCache;
        });
        return services;
    }
}
