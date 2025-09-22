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
    private readonly bool isEnabled;

    public TheaLogger(string name, IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerProcessor processor)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        this.appId = configuration["AppId"];
        this.isEnabled = configuration.GetValue("Logging:IsEnabled", false);
        this.logLevel = configuration.GetValue("Logging:LogLevel:Default", LogLevel.Information);
        this.aspnetLogLevel = configuration.GetValue("Logging:LogLevel:Microsoft.AspNetCore", LogLevel.Error);
        if (appId == null) throw new ArgumentNullException(nameof(appId));
        this.name = name;
        this.environment = hostEnvironment.EnvironmentName;
        this.processor = processor;
    }
    public IDisposable BeginScope<TState>(TState state)
    {
        if (state == null || state is not LogEntity logEntityInfo)
            return null;

        ScopeState.Push(this.Initialize(logEntityInfo));
        return new ScopeStateHolder(ScopeState.Pop);
    }
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!this.isEnabled) return;
        if (formatter == null)
            throw new ArgumentNullException(nameof(formatter));

        //忽略 Microsoft 内部日志
        var stateType = state.GetType();
        if (!this.IsEnabled(logLevel) || stateType.FullName.StartsWith("Microsoft.Extensions.Logging.LoggerMessage"))
            return;

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
        this.Initialize(logEntityInfo);
        //有手动传进来的耗时，不再计算
        if (!logEntityInfo.Elapsed.HasValue)
            logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.LogTime).TotalMilliseconds;
        this.processor.Execute(logEntityInfo);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (!this.name.StartsWith("Thea") && logLevel < this.aspnetLogLevel)
            return false;
        return logLevel >= this.logLevel;
    }
    private LogEntity Initialize(LogEntity logEntityInfo)
    {
        if (!ScopeState.TryGetState(out var lastScopeState))
            return logEntityInfo;

        if (string.IsNullOrEmpty(logEntityInfo.TraceId) && !string.IsNullOrEmpty(lastScopeState.TraceId))
            logEntityInfo.TraceId = lastScopeState.TraceId;
        if (string.IsNullOrEmpty(logEntityInfo.Tag) && !string.IsNullOrEmpty(lastScopeState.Tag))
            logEntityInfo.Tag = lastScopeState.Tag;

        if (string.IsNullOrEmpty(logEntityInfo.TenantId) && !string.IsNullOrEmpty(lastScopeState.TenantId))
            logEntityInfo.TenantId = lastScopeState.TenantId;
        if (string.IsNullOrEmpty(logEntityInfo.UserId) && !string.IsNullOrEmpty(lastScopeState.UserId))
            logEntityInfo.UserId = lastScopeState.UserId;
        if (string.IsNullOrEmpty(logEntityInfo.UserName) && !string.IsNullOrEmpty(lastScopeState.UserName))
            logEntityInfo.UserName = lastScopeState.UserName;
        if (string.IsNullOrEmpty(logEntityInfo.Authorization) && !string.IsNullOrEmpty(lastScopeState.Authorization))
            logEntityInfo.Authorization = lastScopeState.Authorization;

        if (string.IsNullOrEmpty(logEntityInfo.ApiUrl) && !string.IsNullOrEmpty(lastScopeState.ApiUrl))
            logEntityInfo.ApiUrl = lastScopeState.ApiUrl;
        if (string.IsNullOrEmpty(logEntityInfo.Headers) && !string.IsNullOrEmpty(lastScopeState.Headers))
            logEntityInfo.Headers = lastScopeState.Headers;
        if (string.IsNullOrEmpty(logEntityInfo.Request) && !string.IsNullOrEmpty(lastScopeState.Request))
            logEntityInfo.Request = lastScopeState.Request;

        if (string.IsNullOrEmpty(logEntityInfo.Host) && !string.IsNullOrEmpty(lastScopeState.Host))
            logEntityInfo.Host = lastScopeState.Host;
        if (string.IsNullOrEmpty(logEntityInfo.ClientIp) && !string.IsNullOrEmpty(lastScopeState.ClientIp))
            logEntityInfo.ClientIp = lastScopeState.ClientIp;
        return logEntityInfo;
    }
    private struct ScopeStateHolder : IDisposable
    {
        private readonly Action onDispose;
        public ScopeStateHolder(Action onDispose) => this.onDispose = onDispose;
        public void Dispose() => this.onDispose.Invoke();
    }
}