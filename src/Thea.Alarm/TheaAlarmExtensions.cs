using Microsoft.Extensions.DependencyInjection;

namespace Thea.Alarm;

public static class TheaAlarmExtensions
{
    public static IServiceCollection AddTheaAlarm(this IServiceCollection service)
    {
        service.AddSingleton<IAlarmService, DingtalkAlarmService>();
        return service;
    }
}
