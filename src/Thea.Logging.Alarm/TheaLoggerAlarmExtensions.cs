using Microsoft.AspNetCore.Builder;

namespace Thea.Logging.Alarm;

public static class TheaLoggerAlarmExtensions
{
    public static IApplicationBuilder UseTheaLoggerAlarm(this IApplicationBuilder app)
    {
        return app.UseTheaLogHandler<TheaLoggerAlarmMiddleware>();
    }
}