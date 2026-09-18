using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace Thea.Logging;

public class TheaLogger : ILogger
{
    private readonly string name;
    private readonly string appId;
    private readonly string environment;
    private readonly LogLevel logLevel;
    private readonly LogLevel aspnetLogLevel;
    private readonly ILoggerProcessor processor;
    private readonly bool isEnabled;

    public TheaLogger(string name, IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerProcessor processor)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
		        this.name = name;
        this.appId = configuration["AppId"];
        this.isEnabled = configuration.GetValue("Logging:IsEnabled", false);
        this.logLevel = configuration.GetValue("Logging:LogLevel:Default", LogLevel.Information);
        this.aspnetLogLevel = configuration.GetValue("Logging:LogLevel:Microsoft.AspNetCore", LogLevel.Error);
        if (appId == null) throw new ArgumentNullException(nameof(appId));
        this.environment = hostEnvironment.EnvironmentName;
        this.processor = processor;
    }
    public IDisposable BeginScope<TState>(TState state)
    {
        if (state == null || state is not LogEntity logEntityInfo)
            return null;
        ScopeLogger.Update(logEntityInfo);
        return new ScopeLoggerHolder(ScopeLogger.Release);
    }
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!this.isEnabled) return;
        if (!this.IsEnabled(logLevel)) return;
        if (formatter == null)
            throw new ArgumentNullException(nameof(formatter));

        //忽略 Microsoft 内部日志
        var stateType = state.GetType();
        if (stateType.FullName.StartsWith("Microsoft.Extensions.Logging.LoggerMessage"))
            return;

        var hasScope = ScopeLogger.TryGetScope(out var lastLogEntityInfo);
        if (hasScope && !lastLogEntityInfo.IsEnabled) return;

        var logEntityInfo = state as LogEntity;
        if (logEntityInfo == null)
        {
            logEntityInfo = new LogEntity
            {
                Id = ObjectId.NewId(),
                ApiType = (int)ApiType.LocalInvoke,
                Body = formatter.Invoke(state, exception),
                LogLevel = (int)logLevel,
                Exception = exception
            };
        }
        logEntityInfo.AppId = this.appId;
        logEntityInfo.Environment = this.environment;
        if (hasScope) logEntityInfo.LoadFrom(lastLogEntityInfo);
        //有手动传进来的耗时，不再计算
        if (!logEntityInfo.Elapsed.HasValue)
        {
            var nowTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            logEntityInfo.Elapsed = (int)(nowTimestamp - logEntityInfo.LogTime);
        }
        this.processor.ExecuteAsync(logEntityInfo).Wait();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (this.name == "Microsoft.AspNetCore" && logLevel < this.aspnetLogLevel)
            return false;
        var hasScope = ScopeLogger.TryGetScope(out var scopeLogger);
        if (hasScope && !scopeLogger.IsEnabled)
            return false;
        return logLevel >= this.logLevel;
    }
    private struct ScopeLoggerHolder : IDisposable
    {
        private readonly Func<LogEntity> onDispose;
        public ScopeLoggerHolder(Func<LogEntity> onDispose) => this.onDispose = onDispose;
        public void Dispose() => this.onDispose.Invoke();
    }
}