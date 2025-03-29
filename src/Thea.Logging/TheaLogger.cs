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
        logEntityInfo.AppId = this.appId;
        logEntityInfo.Environment = this.environment;
        if (StateScope.State != null)
        {
            var stateScope = StateScope.State;
            if (string.IsNullOrEmpty(logEntityInfo.TraceId) && !string.IsNullOrEmpty(stateScope.TraceId))
                logEntityInfo.TraceId = stateScope.TraceId;
            if (string.IsNullOrEmpty(logEntityInfo.Tag) && !string.IsNullOrEmpty(stateScope.Tag))
                logEntityInfo.Tag = stateScope.Tag;

            if (string.IsNullOrEmpty(logEntityInfo.TenantId) && !string.IsNullOrEmpty(stateScope.TenantId))
                logEntityInfo.TenantId = stateScope.TenantId;
            if (string.IsNullOrEmpty(logEntityInfo.UserId) && !string.IsNullOrEmpty(stateScope.UserId))
                logEntityInfo.UserId = stateScope.UserId;
            if (string.IsNullOrEmpty(logEntityInfo.UserName) && !string.IsNullOrEmpty(stateScope.UserName))
                logEntityInfo.UserName = stateScope.UserName;
            if (string.IsNullOrEmpty(logEntityInfo.Authorization) && !string.IsNullOrEmpty(stateScope.Authorization))
                logEntityInfo.Authorization = stateScope.Authorization;

            if (string.IsNullOrEmpty(logEntityInfo.ApiUrl) && !string.IsNullOrEmpty(stateScope.ApiUrl))
                logEntityInfo.ApiUrl = stateScope.ApiUrl;
            if (string.IsNullOrEmpty(logEntityInfo.Headers) && !string.IsNullOrEmpty(stateScope.Headers))
                logEntityInfo.Headers = stateScope.Headers;
            if (string.IsNullOrEmpty(logEntityInfo.Parameters) && !string.IsNullOrEmpty(stateScope.Parameters))
                logEntityInfo.Parameters = stateScope.Parameters;

            if (string.IsNullOrEmpty(logEntityInfo.Host) && !string.IsNullOrEmpty(stateScope.Host))
                logEntityInfo.Host = stateScope.Host;
            if (string.IsNullOrEmpty(logEntityInfo.ClientIp) && !string.IsNullOrEmpty(stateScope.ClientIp))
                logEntityInfo.ClientIp = stateScope.ClientIp;

            //设置基础信息
            if (!string.IsNullOrEmpty(stateScope.TraceId))
                stateScope.TraceId = logEntityInfo.TraceId;
            if (!string.IsNullOrEmpty(logEntityInfo.Tag))
                stateScope.Tag = logEntityInfo.Tag;
            if (!string.IsNullOrEmpty(logEntityInfo.TenantId))
                stateScope.TenantId = logEntityInfo.TenantId;
            if (!string.IsNullOrEmpty(logEntityInfo.UserId))
                stateScope.UserId = logEntityInfo.UserId;
            if (!string.IsNullOrEmpty(logEntityInfo.UserName))
                stateScope.UserName = logEntityInfo.UserName;
            if (!string.IsNullOrEmpty(logEntityInfo.Authorization))
                stateScope.Authorization = logEntityInfo.Authorization;
            if (!string.IsNullOrEmpty(logEntityInfo.ApiUrl))
                stateScope.ApiUrl = logEntityInfo.ApiUrl;
            if (!string.IsNullOrEmpty(logEntityInfo.Headers))
                stateScope.Headers = logEntityInfo.Headers;
            if (!string.IsNullOrEmpty(logEntityInfo.Parameters))
                stateScope.Parameters = logEntityInfo.Parameters;
        }
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
        if (state is LogEntity logEntity)
            return StateScope.Push(logEntity);
        return null;
    }
}
public class TheaLogger<T> : TheaLogger
{
    public TheaLogger(string name, IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerProcessor processor)
        : base(name, configuration, hostEnvironment, processor) { }
}
