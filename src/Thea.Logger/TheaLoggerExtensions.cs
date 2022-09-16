using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Thea.Logger;

public static class TheaLoggerExtensions
{
    public static IServiceCollection AddTheaLogger(this IServiceCollection services)
    {
        services.AddSingleton<ILoggerHandlerBuilder, TheaLoggerHandlerBuilder>();
        services.AddSingleton<TheaLoggerProcessor>(); 
        services.AddSingleton<ILoggerProvider, TheaLoggerProvider>();
        return services;
    }

    //public static ILoggingBuilder AddTheaLogger(this ILoggingBuilder builder)
    //{
    //    builder.AddConfiguration();
    //    //builder.ClearProviders();
    //    builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TheaLoggerProvider>());
    //    return builder;
    //}
    public static IApplicationBuilder UseTheaLogHandler(this IApplicationBuilder app, Action<ILoggerHandlerBuilder> builderInitializer = null)
    {
        var handlerBuilder = app.ApplicationServices.GetService<ILoggerHandlerBuilder>();
        var loggerProcessor = app.ApplicationServices.GetService<TheaLoggerProcessor>();

        builderInitializer?.Invoke(handlerBuilder);
        loggerProcessor.Build();

        return app;
    }
}
