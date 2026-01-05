using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Thea.Alarm;

namespace Thea.Logging.Alarm;

public class TheaLogAlarmMiddleware
{
    private readonly IAlarmService alarmService;
    private readonly string logVisitUrl;
    private readonly LoggerHandlerDelegate next;
    private readonly ConcurrentDictionary<int, AlarmInfo> alarmInfos = new();
    private readonly int alarmLevel = (int)LogLevel.Warning;

    public TheaLogAlarmMiddleware(LoggerHandlerDelegate next, IAlarmService alarmService, IConfiguration configuration, ILogger<TheaLogAlarmMiddleware> logger)
    {
        this.next = next;
        this.alarmService = alarmService;
        this.logVisitUrl = configuration.GetValue<string>("Alarm:LogWebSite");
        if (string.IsNullOrEmpty(this.logVisitUrl))
            throw new ArgumentNullException("appsettings.json not found 'Alarm:LogWebSite' node or value is null.");
        var logLevel = configuration.GetValue<string>("Alarm:LogLevel");
        if (!string.IsNullOrEmpty(logLevel))
        {
            if (Enum.TryParse<LogLevel>(logLevel, out var level))
                this.alarmLevel = (int)level;
            else logger.LogWarning($"The LogLevel value '{logLevel}' is invalid, the default value is 'Warning'.");
        }
    }
    public async Task Invoke(LogEntity logEntityInfo)
    {
        if (logEntityInfo.LogLevel < this.alarmLevel)
        {
            await this.next(logEntityInfo);
            return;
        }

        var body = logEntityInfo.Exception?.ToString() ?? logEntityInfo.Body ?? logEntityInfo.Response;
        var hashKey = HashCode.Combine(logEntityInfo.AppId, logEntityInfo.ApiUrl, body);
        if (!this.alarmInfos.TryGetValue(hashKey, out var alarmInfo))
        {
            this.alarmInfos.TryAdd(hashKey, alarmInfo = new AlarmInfo
            {
                CreatedAt = DateTime.Now,
                FiredTimes = 1
            });
            this.Build(logEntityInfo, alarmInfo);
            await this.alarmService.PostAsync(alarmInfo.Header, alarmInfo.Content);
        }
        else alarmInfo.FiredTimes++;

        //过十分钟了，再报一次，同时更新时间
        if (DateTime.Now.Subtract(alarmInfo.CreatedAt) > TimeSpan.FromMinutes(10))
        {
            alarmInfo.FiredTimes++;
            this.Build(logEntityInfo, alarmInfo);
            await this.alarmService.PostAsync(alarmInfo.Header, alarmInfo.Content);
            //十分钟后移除
            this.alarmInfos.TryRemove(hashKey, out _);
        }
        else alarmInfo.FiredTimes++;

        //十分钟后再报一次，并删除报警信息，防止占用太多内存
        var removeKeys = new List<int>();
        foreach (var key in this.alarmInfos.Keys)
        {
            var myAlarmInfo = this.alarmInfos[key];
            if (DateTime.Now.Subtract(myAlarmInfo.CreatedAt) > TimeSpan.FromMinutes(10))
            {
                if (myAlarmInfo.FiredTimes > 1)
                    await this.alarmService.PostAsync(alarmInfo.Header, alarmInfo.Content);
                removeKeys.Add(key);
            }
        }
        if (removeKeys.Count > 0)
            removeKeys.ForEach(key => this.alarmInfos.TryRemove(key, out _));

        await this.next(logEntityInfo);
    }
    private void Build(LogEntity logEntityInfo, AlarmInfo alarmInfo)
    {
        Func<string, string> escape = mesage => this.alarmService.Escape(mesage);
        alarmInfo.Header = "告警";
        if (logEntityInfo.LogLevel > (int)LogLevel.Warning)
            alarmInfo.Header = "异常告警";

        var contentBuilder = new StringBuilder()
            .AppendLine("**日志信息** ")
            .AppendLine($"> TraceId：{logEntityInfo.TraceId}")
            .AppendLine($"> 环  境：{logEntityInfo.Environment}")
            .AppendLine($"> 应用ID：{logEntityInfo.AppId}")
            .AppendLine($"> 用户ID：{logEntityInfo.UserId}")
            .AppendLine($"> Headers：{escape(logEntityInfo.Headers)}")
            .AppendLine($"> 耗  时：{logEntityInfo.Elapsed} ms")
            .AppendLine($"> Api地址：{escape(logEntityInfo.ApiUrl)}")
            .AppendLine($"> 认证信息：{escape(logEntityInfo.Authorization)}")
            .AppendLine($"> 请求参数：{escape(logEntityInfo.Request)}");
        if (logEntityInfo.Exception == null)
            contentBuilder.AppendLine($"> 响应内容：{escape(logEntityInfo.Response)}");
        contentBuilder.AppendLine($"> 发生时间：{logEntityInfo.LogTime:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"> 触发次数：{alarmInfo.FiredTimes}").AppendLine();
        if (logEntityInfo.Exception != null)
            contentBuilder.AppendLine("**异常内容**").AppendLine($"{escape(logEntityInfo.Exception.ToString())}");
        else contentBuilder.AppendLine("**详细内容**").AppendLine($"{escape(logEntityInfo.Body)}");
        alarmInfo.Content = contentBuilder.ToString();
    }
    class AlarmInfo
    {
        public DateTime CreatedAt { get; set; }
        public int FiredTimes { get; set; }
        public string Header { get; set; }
        public string Content { get; set; }
    }
}