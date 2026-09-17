using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Thea.Alarm;

namespace Thea.Logging.Alarm;

public class TheaLogAlarmMiddleware
{
    private readonly IAlarmService alarmService;
    //private readonly string logVisitUrl;
    private readonly LoggerHandlerDelegate next;
    private readonly int alarmLevel = (int)LogLevel.Warning;
    private readonly List<string> ignoreKeywords;

    public TheaLogAlarmMiddleware(LoggerHandlerDelegate next, IAlarmService alarmService, IConfiguration configuration, ILogger<TheaLogAlarmMiddleware> logger)
    {
        this.next = next;
        this.alarmService = alarmService;
        //this.logVisitUrl = configuration.GetValue<string>("Alarm:LogWebSite");
        var logLevel = configuration.GetValue<string>("Alarm:LogLevel");
        if (!string.IsNullOrEmpty(logLevel))
        {
            if (Enum.TryParse<LogLevel>(logLevel, out var level))
                this.alarmLevel = (int)level;
            else logger.LogWarning($"The LogLevel value '{logLevel}' is invalid, the default value is 'Warning'.");
        }
        this.ignoreKeywords = configuration.GetSection("Alarm:IngoreKeywords").Get<List<string>>();
    }
    public async Task Invoke(LogEntity logEntityInfo)
    {
        try
        {
            if (logEntityInfo == null || logEntityInfo.LogLevel < this.alarmLevel)
            {
                await this.next(logEntityInfo);
                return;
            }
            var body = logEntityInfo.Exception?.ToString() ?? logEntityInfo.Body ?? logEntityInfo.Response;
            var senceKey = HashCode.Combine(logEntityInfo.AppId, logEntityInfo.ApiUrl, body);
            var title = "日志信息";
            var level = 0;
            if (logEntityInfo.LogLevel >= (int)LogLevel.Warning)
            {
                // 过滤掉一些不需要告警的关键词，比如特定的错误码等，减少不必要的告警
                if (this.ignoreKeywords != null)
                {
                    var response = logEntityInfo.Response.JsonTo<TheaResponse>();
                    var myKeyword = response?.Code.ToString();
                    if (!string.IsNullOrEmpty(myKeyword) && this.ignoreKeywords.Contains(myKeyword))
                    {
                        await this.next(logEntityInfo);
                        return;
                    }
                }
            }
            if (logEntityInfo.LogLevel > (int)LogLevel.Warning)
            {
                title = "异常告警";
                level = 1;
            }
            var logTime = DateTimeOffset.FromUnixTimeMilliseconds(logEntityInfo.LogTime);
            var environment = logEntityInfo.Environment;
            var content = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("TraceId", logEntityInfo.TraceId),
                new KeyValuePair<string, string>("环  境", environment),
                new KeyValuePair<string, string>("应用ID", logEntityInfo.AppId),
                new KeyValuePair<string, string>("商户ID", $"{logEntityInfo.TenantId}"),
                new KeyValuePair<string, string>("用户ID", logEntityInfo.UserId),
                new KeyValuePair<string, string>("Headers", logEntityInfo.Headers),
                new KeyValuePair<string, string>("耗  时", $"{logEntityInfo.Elapsed} ms"),
                new KeyValuePair<string, string>("Api地址", logEntityInfo.ApiUrl),
                new KeyValuePair<string, string>("认证信息", logEntityInfo.Authorization),
                new KeyValuePair<string, string>("日志内容", logEntityInfo.Body),
                new KeyValuePair<string, string>("请求参数", logEntityInfo.Request)
            };
            if (logEntityInfo.Exception != null)
                content.Add(new KeyValuePair<string, string>("异常内容", logEntityInfo.Exception.ToString()));
            else content.Add(new KeyValuePair<string, string>("响应内容", logEntityInfo.Response));
            content.Add(new KeyValuePair<string, string>("发生时间", logTime.ToString("yyyy-MM-dd HH:mm:ss")));
            await this.alarmService.PostAsync(new AlarmRequest
            {
                AppId = logEntityInfo.AppId,
                ChannelId = "LogAlarm",
                Level = level,
                SenceKey = senceKey,
                Title = title,
                Content = content
            });
            await this.next(logEntityInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TheaLogAlarmMiddleware exception, Exception: {ex}, logEntityInfo: {logEntityInfo.ToJson()}");
        }
    }
}