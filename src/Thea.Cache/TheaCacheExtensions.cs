using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Thea.Cache;

public static class TheaCacheExtensions
{
    public static IServiceCollection AddRedisCache(this IServiceCollection services, Func<string, int> databaseSelector = null)
    {
        services.AddSingleton<IDistributedCache>(f =>
        {
            var configuration = f.GetRequiredService<IConfiguration>();
            var redisCache = new RedisCache(configuration);
            if (databaseSelector != null)
                redisCache.UserDatabase(databaseSelector);
            return redisCache;
        });
        return services;
    }
}
