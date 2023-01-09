using Microsoft.AspNetCore.Builder;

namespace Thea.Logging.Alarm;

public static class TheaLogAlarmExtensions
{
    public static IApplicationBuilder UseTheaLogAlarm(this IApplicationBuilder app)
    {
        return app.UseTheaLogHandler<TheaLogAlarmMiddleware>();
    }
}