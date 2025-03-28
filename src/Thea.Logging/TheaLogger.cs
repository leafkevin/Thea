using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Thea.Logging;

public class TheaLogger : ILogger
{
    private readonly string name;
    private readonly string appId;
    private readonly string environment;
    private readonly LogLevel logLevel;
    private readonly LogLevel aspnetLogLevel;
    private readonly ILoggerProcessor processor;

    public TheaLogger(string name, IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerProcessor processor)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        this.appId = configuration["AppId"];
        this.logLevel = configuration.GetValue("Logging:LogLevel:Default", LogLevel.Information);
        this.aspnetLogLevel = configuration.GetValue("Logging:LogLevel:Microsoft.AspNetCore", LogLevel.Error);
        if (appId == null) throw new ArgumentNullException(nameof(appId));
        this.name = name;
        this.environment = hostEnvironment.EnvironmentName;
        this.processor = processor;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!this.IsEnabled(logLevel)) return;
        if (formatter == null)
            throw new ArgumentNullException(nameof(formatter));

        var logEntityInfo = state as LogEntity;
        if (logEntityInfo == null)
        {
            logEntityInfo = new LogEntity
            {
                Id = ObjectId.NewId(),
                AppId = this.appId,
                Body = formatter.Invoke(state, exception),
                LogLevel = (int)logLevel,
                Exception = exception
            };
        }
        if (string.IsNullOrEmpty(logEntityInfo.AppId))
            logEntityInfo.AppId = this.appId;
        if (!string.IsNullOrEmpty(StateScope.TraceId))
            logEntityInfo.TraceId = StateScope.TraceId;
        if (string.IsNullOrEmpty(logEntityInfo.Environment))
            logEntityInfo.Environment = this.environment;
        if (!logEntityInfo.Elapsed.HasValue)
            logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.LogTime).TotalMilliseconds;

        this.processor.Execute(logEntityInfo);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (this.name.Contains("Microsoft.AspNetCore") && logLevel < this.aspnetLogLevel)
            return false;
        return logLevel >= this.logLevel;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        if (state is string traceId)
            return StateScope.Push(traceId);
        //其他类型暂时不处理，没有意义
        return null;
    }
}
public class TheaLogger<T> : TheaLogger
{
    public TheaLogger(string name, IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerProcessor processor)
        : base(name, configuration, hostEnvironment, processor) { }
}
