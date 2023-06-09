using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Thea.Logging.Alarm;

public static class TheaLogAlarmExtensions
{
    public static IApplicationBuilder UseTheaLogAlarm(this IApplicationBuilder app)
    {
        var loggerProcessor = app.ApplicationServices.GetService<ILoggerProcessor>();
        loggerProcessor.AddHandler<TheaLogAlarmMiddleware>();
        loggerProcessor.Build();
        return app;
    }
}