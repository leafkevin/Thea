using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Thea.Auth;
using Thea.Cache;
using Thea.MessageDriven;
using Thea.MessageDriven.MySqlRepository;
using Thea.Web;
using Trolley;
using WebAppTest.Domain.Services;

namespace WebAppTest.Domain;

public static class Startup
{
    public static void AddDomainServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(f =>
        {
            var connString = configuration["ConnectionStrings:default"];
            return new OrmDbFactoryBuilder()
                .Register(OrmProviderType.MySql, "default", connString, true)
                .Configure<ModelConfiguration>(OrmProviderType.MySql)
                .Build();
        });
        services.AddMemoryCache();
        services.AddRedisCache();
        services.AddTheaWeb();
        services.AddPassport();
        //services.AddTheaLogging();
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
        services.AddMessageDriven();
        services.AddSingleton<StatefulConsumer>();

        //var frontendUrl = configuration["FrontendUrl"];
        //string[] urls = new[] { frontendUrl };
        //services.AddCors(options => options.AddDefaultPolicy(policy =>
        //    policy.WithOrigins(urls).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    }
    public static void UseDomainServices(this IApplicationBuilder app, IConfiguration configuration)
    {
        app.UseTheaWeb();
        var memoryCache = app.ApplicationServices.GetService<IMemoryCache>();
        //内存缓存更新
        app.UseMessageDriven(f =>
        {
            f.UseTrolleyRepository("default");
            f.UseProducer("award.take", true);
            //f.UseSubscriber<string>("cache.refresh", "cache.refresh.queue",
            //    key => { memoryCache.Remove(key); return Task.CompletedTask; })
            f.UseStatefulConsumer<StatefulConsumer>("award.take", f => f.TakeAward)
            .UseStatefulConsumer<StatefulConsumer>("award.issue", f => f.IssueAward);
        });
    }
}