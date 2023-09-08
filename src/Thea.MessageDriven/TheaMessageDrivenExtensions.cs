using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using Thea.Orm;
using Thea.Trolley.MySqlConnector;

namespace Thea.MessageDriven;

public static class TheaMessageDrivenExtensions
{
    public static IServiceCollection AddMessageDriven(this IServiceCollection services)
    {
        services.AddSingleton<IMessageDriven, MessageDrivenService>();
        return services;
    }
    public static IApplicationBuilder UseMessageDriven(this IApplicationBuilder app, Action<MessageDrivenBuilder> builderInitializer)
    {
        var dbFactory = app.ApplicationServices.GetService<IOrmDbFactory>();
        dbFactory.Configure(typeof(MySqlProvider), new ModelConfiguration());
        dbFactory.Build(typeof(MySqlProvider));
        var service = app.ApplicationServices.GetService<IMessageDriven>();
        var builder = new MessageDrivenBuilder(app.ApplicationServices);
        builderInitializer.Invoke(builder);
        service.Start();
        return app;
    }
}
