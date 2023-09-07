using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using Thea.Orm;

namespace Thea.MessageDriven;

public static class TheaMessageDrivenExtensions
{
    public static IServiceCollection AddMessageDriven(this IServiceCollection services)
    {
        services.AddSingleton<IMessageDriven, MessageDrivenService>();
        return services;
    }
    public static IApplicationBuilder UseMessageDriven<TOrmProvider>(this IApplicationBuilder app, Action<MessageDrivenBuilder> builderInitializer) where TOrmProvider : IOrmProvider
    {
        var dbFactory = app.ApplicationServices.GetService<IOrmDbFactory>();
        dbFactory.Configure(typeof(TOrmProvider), new ModelConfiguration());
        var service = app.ApplicationServices.GetService<IMessageDriven>();
        var builder = new MessageDrivenBuilder(app.ApplicationServices);
        builderInitializer.Invoke(builder);
        service.Start();
        return app;
    }
}
