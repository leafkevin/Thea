using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Thea.Logging;

public static class TheaLoggingExtensions
{
    public static IServiceCollection AddTheaLogging(this IServiceCollection services)
    {
        services.AddSingleton<ILoggerProcessor, TheaLoggerProcessor>();
        services.AddSingleton<ILoggerProvider, TheaLoggerProvider>();
        return services;
    }
    public static IApplicationBuilder UseTheaLogHandler(this IApplicationBuilder app, Action<ILoggerProcessor> builderInitializer = null)
    {
        var loggerProcessor = app.ApplicationServices.GetService<ILoggerProcessor>();
        builderInitializer?.Invoke(loggerProcessor);
        loggerProcessor.Build();
        return app;
    }
    public static IApplicationBuilder UseTheaLogHandler<TLogHandlerMiddleware>(this IApplicationBuilder app)
    {
        var loggerProcessor = app.ApplicationServices.GetService<ILoggerProcessor>();
        loggerProcessor.AddHandler<TLogHandlerMiddleware>();
        loggerProcessor.Build();
        return app;
    }
}
