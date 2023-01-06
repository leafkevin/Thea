using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Thea.Logging;

public class TheaLogger : ILogger
{
    private static byte[] _rgbKey = ASCIIEncoding.ASCII.GetBytes("Thea.Log");
    private static byte[] _rgbIV = ASCIIEncoding.ASCII.GetBytes("Thea.Log");

    private readonly string name;
    private readonly string appId;
    private readonly LogLevel logLevel;
    private readonly LogLevel microsoftLogLevel;
    private readonly LogLevel systemLogLevel;
    private readonly ILoggerProcessor processor;

    public TheaLogger(string name, IConfiguration configuration, ILoggerProcessor processor)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        this.appId = configuration["AppId"];
        this.logLevel = configuration.GetValue<LogLevel>("Logging:LogLevel:Default", LogLevel.Information);
        this.systemLogLevel = configuration.GetValue<LogLevel>("Logging:LogLevel:System", LogLevel.Error);
        this.microsoftLogLevel = configuration.GetValue<LogLevel>("Logging:LogLevel:Microsoft", LogLevel.Error);
        if (appId == null) throw new ArgumentNullException(nameof(appId));
        this.name = name;
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
                LogLevel = logLevel,
                Exception = exception
            };
        }
        if (string.IsNullOrEmpty(logEntityInfo.AppId))
            logEntityInfo.AppId = this.appId;
        if (!logEntityInfo.Elapsed.HasValue)
            logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.CreatedAt).TotalMilliseconds;

        logEntityInfo.LogTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        if (!string.IsNullOrEmpty(logEntityInfo.Authorization))
            logEntityInfo.Authorization = Encrypt(logEntityInfo.Authorization);

        if (TheaLogScope.Current != null && TheaLogScope.Current.State != null)
        {
            if (string.IsNullOrEmpty(logEntityInfo.TraceId))
                logEntityInfo.TraceId = TheaLogScope.Current.State.TraceId;
            if (string.IsNullOrEmpty(logEntityInfo.Tag))
                logEntityInfo.Tag = TheaLogScope.Current.State.Tag;

            logEntityInfo.Sequence = TheaLogScope.Current.State.Sequence;
            TheaLogScope.Current.State.Sequence++;
        }
        this.processor.Execute(logEntityInfo);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (this.name.Contains("Microsoft") && logLevel < this.microsoftLogLevel)
            return false;
        if (this.name.Contains("System") && logLevel < this.systemLogLevel)
            return false;

        return logLevel >= this.logLevel;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        if (state is TheaLogState logState)
            return TheaLogScope.Push(logState);
        else if (state is LogEntity logEntity)
        {
            return TheaLogScope.Push(new TheaLogState
            {
                TraceId = logEntity.TraceId,
                Tag = logEntity.Tag
            });
        }
        //其他类型暂时不处理，没有意义
        return null;
    }
    private static string Encrypt(string content)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        var dsp = DES.Create();
        using var memStream = new MemoryStream();
        using var crypStream = new CryptoStream(memStream, dsp.CreateEncryptor(_rgbKey, _rgbIV), CryptoStreamMode.Write);
        var sWriter = new StreamWriter(crypStream);
        sWriter.Write(content);
        sWriter.Flush();
        crypStream.FlushFinalBlock();
        memStream.Flush();
        return Convert.ToBase64String(memStream.GetBuffer(), 0, (int)memStream.Length);
    }
}

public class TheaLogger<T> : TheaLogger
{
    public TheaLogger(string name, IConfiguration configuration, ILoggerProcessor processor)
        : base(name, configuration, processor) { }
}
