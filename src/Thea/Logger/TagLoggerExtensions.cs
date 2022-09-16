using Microsoft.Extensions.Logging;
using System;

namespace Thea.Logger;

public static class TagLoggerExtensions
{
    private static string LogEntityFormatter(LogEntity state, Exception ex) => state.ToString();
    public static void AddLogState(this ILogger logger, string traceId, string tag)
        => logger.BeginScope(new TheaLogState { TraceId = traceId, Tag = tag });
    public static void AddTraceId(this ILogger logger, string traceId)
        => logger.BeginScope(new TheaLogState { TraceId = traceId });
    public static void AddTag(this ILogger logger, string tag)
        => logger.BeginScope(new TheaLogState { Tag = tag });
    public static void LogEntity(this ILogger logger, LogEntity logEntityInfo)
        => logger.Log<LogEntity>(logEntityInfo.LogLevel, 0, logEntityInfo, logEntityInfo.Exception, LogEntityFormatter);
}
