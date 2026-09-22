using Microsoft.Extensions.DependencyInjection;

namespace Thea.Alarm;

public static class TheaAlarmExtensions
{
    public static IServiceCollection AddTheaAlarm<TAlarmService>(this IServiceCollection service)
        where TAlarmService : class, IAlarmService
        => service.AddSingleton<IAlarmService, TAlarmService>();
    public static IServiceCollection AddKeyedTheaAlarm<TAlarmService>(this IServiceCollection service)
        where TAlarmService : class, IAlarmService
        => service.AddKeyedSingleton<IAlarmService, TAlarmService>(typeof(TAlarmService));
}