using Microsoft.Extensions.DependencyInjection;
using System;

namespace Thea.Alarm;

public static class TheaAlarmExtensions
{
    public static IServiceCollection AddTheaAlarm<TAlarmService>(this IServiceCollection service)
        where TAlarmService : class, IAlarmService
        => service.AddSingleton<IAlarmService, TAlarmService>();
    public static IServiceCollection AddKeyedTheaAlarm<TAlarmService>(this IServiceCollection service, Func<IServiceProvider, object, TAlarmService> implGetter = null)
        where TAlarmService : class, IAlarmService
    {
        if (implGetter != null)
            return service.AddKeyedSingleton<IAlarmService, TAlarmService>(typeof(TAlarmService), implGetter);
        return service.AddKeyedSingleton<IAlarmService, TAlarmService>(typeof(TAlarmService));
    }
}