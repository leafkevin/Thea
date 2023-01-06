using Microsoft.Extensions.Logging;
using System;

namespace Thea.Logging;

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
        => logger.Log(logEntityInfo.LogLevel, 0, logEntityInfo, logEntityInfo.Exception, LogEntityFormatter);


    public static void LogTagDebug(this ILogger logger, EventId eventId, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Debug, eventId, tag, exception, message, args);
    public static void LogTagDebug(this ILogger logger, EventId eventId, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Debug, eventId, tag, message, args);
    public static void LogTagDebug(this ILogger logger, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Debug, tag, exception, message, args);
    public static void LogTagDebug(this ILogger logger, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Debug, tag, message, args);
    public static void LogTagTrace(this ILogger logger, EventId eventId, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Trace, eventId, tag, exception, message, args);
    public static void LogTagTrace(this ILogger logger, EventId eventId, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Trace, eventId, tag, message, args);
    public static void LogTagTrace(this ILogger logger, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Trace, tag, exception, message, args);
    public static void LogTagTrace(this ILogger logger, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Trace, tag, message, args);
    public static void LogTagInformation(this ILogger logger, EventId eventId, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Information, eventId, tag, exception, message, args);
    public static void LogTagInformation(this ILogger logger, EventId eventId, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Information, eventId, tag, message, args);
    public static void LogTagInformation(this ILogger logger, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Information, tag, exception, message, args);
    public static void LogTagInformation(this ILogger logger, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Information, tag, message, args);
    public static void LogTagWarning(this ILogger logger, EventId eventId, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Warning, eventId, tag, exception, message, args);
    public static void LogTagWarning(this ILogger logger, EventId eventId, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Warning, eventId, tag, message, args);
    public static void LogTagWarning(this ILogger logger, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Warning, tag, exception, message, args);
    public static void LogTagWarning(this ILogger logger, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Warning, tag, message, args);
    public static void LogTagError(this ILogger logger, EventId eventId, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Error, eventId, tag, exception, message, args);
    public static void LogTagError(this ILogger logger, EventId eventId, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Error, eventId, tag, message, args);
    public static void LogTagError(this ILogger logger, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Error, tag, exception, message, args);
    public static void LogTagError(this ILogger logger, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Error, tag, message, args);
    public static void LogTagCritical(this ILogger logger, EventId eventId, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Critical, eventId, tag, exception, message, args);
    public static void LogTagCritical(this ILogger logger, EventId eventId, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Critical, eventId, tag, message, args);
    public static void LogTagCritical(this ILogger logger, string tag, Exception exception, string message, params object[] args)
        => logger.Log(LogLevel.Critical, tag, exception, message, args);
    public static void LogTagCritical(this ILogger logger, string tag, string message, params object[] args)
        => logger.Log(LogLevel.Critical, tag, message, args);

    public static void Log(this ILogger logger, LogLevel logLevel, string tag, string message, params object[] args)
        => logger.Log(logLevel, 0, tag, null, message, args);
    public static void Log(this ILogger logger, LogLevel logLevel, EventId eventId, string tag, string message, params object[] args)
        => logger.Log(logLevel, eventId, tag, null, message, args);
    public static void Log(this ILogger logger, LogLevel logLevel, string tag, Exception exception, string message, params object[] args)
        => logger.Log(logLevel, 0, tag, exception, message, args);
    private static void Log(this ILogger logger, LogLevel logLevel, EventId eventId, string tag, Exception exception, string message, params object[] args)
    {
        if (logger == null)
            throw new ArgumentNullException(nameof(logger));
        var body = message;
        if (!string.IsNullOrEmpty(message))
        {
            if (args != null && args.Length > 0)
                body = string.Format(message, args);
        }
        if (exception != null)
        {
            if (string.IsNullOrEmpty(body))
                body = exception.Message;
            else body += Environment.NewLine + "Error Message:" + exception.Message;
        }

        logger.LogEntity(new LogEntity
        {
            Id = ObjectId.NewId(),
            LogLevel = logLevel,
            ApiType = ApiType.LocalInvoke,
            Tag = tag,
            Body = body,
            Exception = exception,
            CreatedAt = DateTime.Now
        });
    }
}
