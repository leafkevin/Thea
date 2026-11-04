using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Thea.Auth;
using Thea.Cache;
using Thea.Logging;
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
                .Register("default", OrmProviderType.MySql, f => f.Use(connString), true)
                .UseMapping<ModelConfiguration>(OrmProviderType.MySql)
                //.UseInterceptor<MyDbInterceptor>()
                .Build();
        });
        services.AddMemoryCache();
        services.AddRedisCache();
        services.AddTheaWeb();
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
        //var memoryCache = app.ApplicationServices.GetService<IMemoryCache>();
        //内存缓存更新
        app.UseMessageDriven(f =>
        {
            f.UseTrolleyRepository("default")
             .UseSubscriber<StatefulConsumer>("cache.refresh", "cache.queue", f => f.RemoveCache)
             .UseStatefulConsumer<StatefulConsumer>("award.take", "user", f => f.TakeAward)
             .UseStatefulConsumer<StatefulConsumer>("award.issue", "user", f => f.IssueAward)
             .UseStatefulConsumer<StatefulConsumer>("sequence", "sequence", f => f.UpdateSequence)
             .UseTransferConsumer(2).UseProducer().UseRpcConsumer();
        });
    }
}