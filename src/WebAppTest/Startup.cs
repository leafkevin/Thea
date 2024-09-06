using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using Thea.Auth;
using Thea.Cache;
using Thea.Logging;
using Thea.MessageDriven;
using Thea.Web;
using Trolley;

namespace WebAppTest.Domain;

public static class Startup
{
    private static int connTotal = 0;
    private static int connOpenTotal = 0;

    public static void AddDomainServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(f =>
        {
            var connString = configuration["ConnectionStrings:default"];
            return new OrmDbFactoryBuilder()
                .Register(OrmProviderType.PostgreSql, "default", connString, true)
                .Configure<ModelConfiguration>(OrmProviderType.PostgreSql)
                .UseInterceptors(df =>
                {
                    df.OnConnectionCreated += evt =>
                    {
                        Interlocked.Increment(ref connTotal);
                        Console.WriteLine($"{evt.ConnectionId} Created, ConnectionString:{evt.ConnectionString}, Total:{Volatile.Read(ref connTotal)}");
                    };
                    df.OnConnectionOpened += evt =>
                    {
                        Interlocked.Increment(ref connOpenTotal);
                        Console.WriteLine($"{evt.ConnectionId} Opened, ConnectionString:{evt.ConnectionString}, Total:{Volatile.Read(ref connOpenTotal)}");
                    };
                    df.OnConnectionClosed += evt =>
                    {
                        Interlocked.Decrement(ref connOpenTotal);
                        Interlocked.Decrement(ref connTotal);
                        Console.WriteLine($"{evt.ConnectionId} Closed, ConnectionString:{evt.ConnectionString}, Total:{Volatile.Read(ref connOpenTotal)}");
                    };
                    df.OnCommandExecuting += evt =>
                    {
                        Console.WriteLine($"{evt.SqlType} Begin, Sql: {evt.Sql}");
                    };
                    df.OnCommandExecuted += evt =>
                    {
                        Console.WriteLine($"{evt.SqlType} End, Elapsed: {evt.Elapsed} ms, Sql: {evt.Sql}");
                    };
                })
                .Build();
        });
        services.AddMemoryCache();
        services.AddRedisCache();
        services.AddTheaWeb();
        services.AddPassport();
        services.AddTheaLogging();
        services.AddTheaAuthentication(f =>
        {
            f.Issuer = "thea";
            f.Audience = "thea";
            f.LifeTime = TimeSpan.FromMinutes(5);
            f.PrivateSecretKey = configuration["Authorization:RsaPrivateKey"];
            f.PublicSecretKey = configuration["Authorization:RsaPublicKey"];
        });

        services.AddTheaWeb();
        services.AddPassport();
        services.AddTheaLogging();
        services.AddMessageDriven();

        //var frontendUrl = configuration["FrontendUrl"];
        //string[] urls = new[] { frontendUrl };
        //services.AddCors(options => options.AddDefaultPolicy(policy =>
        //    policy.WithOrigins(urls).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    }
    public static void UseDomainServices(this IApplicationBuilder app, IConfiguration configuration)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);

        app.UseTheaWeb();
        var memoryCache = app.ApplicationServices.GetService<IMemoryCache>();
        //内存缓存更新
        app.UseMessageDriven(f =>
        {
            f.Create("default")
            .AddProducer("cache.refresh")
            .AddSubscriber<string>("cache.refresh", "cache.refresh.queue",
                key => { memoryCache.Remove(key); return Task.CompletedTask; });
        });
    }
}
