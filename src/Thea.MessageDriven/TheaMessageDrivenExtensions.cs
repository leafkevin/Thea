using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;

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
        var service = app.ApplicationServices.GetService<IMessageDriven>();
        var builder = new MessageDrivenBuilder(app.ApplicationServices);
        builderInitializer.Invoke(builder);
        service.Start();
        return app;
    }
}
