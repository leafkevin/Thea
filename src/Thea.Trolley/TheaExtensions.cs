using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using Thea.Orm;

namespace Thea.Trolley;

public static class TheaExtensions
{
    public static IServiceCollection AddTrolley(this IServiceCollection services)
    {
        services.AddSingleton<IOrmDbFactory, OrmDbFactory>();
        return services;
    }
    public static IApplicationBuilder UseTrolley(this IApplicationBuilder app, Action<OrmDbFactoryBuilder> initializer)
    {
        var dbFactory = app.ApplicationServices.GetService<IOrmDbFactory>();
        var builder = new OrmDbFactoryBuilder(dbFactory);
        initializer?.Invoke(builder);
        return app;
    }

    public static void AddEntityMap<TEntity>(this IOrmDbFactory dbFactory)
    {
        var entityType = typeof(TEntity);
        dbFactory.AddEntityMap(entityType, new EntityMap(entityType));
    }
}

