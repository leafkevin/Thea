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
    public async Task Invoke(LoggerHandlerContext context)
    {
        if (context.LogEntity != null)
        {
            var logEntityInfo = context.LogEntity;
            if (logEntityInfo.LogLevel < this.alarmLevel)
                return;

            var hashKey = HashCode.Combine(logEntityInfo.AppId, logEntityInfo.ApiUrl, logEntityInfo.Body);
            if (!this.alarmInfos.TryGetValue(hashKey, out var alarmInfo))
            {
                this.alarmInfos.TryAdd(hashKey, alarmInfo = new AlarmInfo
                {
                    CreatedAt = DateTime.Now,
                    FiredTimes = 1
                });
                this.Build(logEntityInfo, alarmInfo);
                alarmInfo.SenceKey = $"{logEntityInfo.AppId}_{logEntityInfo.UserId}_{logEntityInfo.ApiUrl}_{logEntityInfo.Body}";
                await this.alarmService.PostAsync(alarmInfo.SenceKey, alarmInfo.Header, alarmInfo.Content);
            }
            else
            {
                //过十分钟了，再报一次，同时更新时间
                if (DateTime.Now.Subtract(alarmInfo.CreatedAt) > TimeSpan.FromMinutes(10))
                {
                    alarmInfo.FiredTimes++;
                    this.Build(logEntityInfo, alarmInfo);
                    await this.alarmService.PostAsync(alarmInfo.SenceKey, alarmInfo.Header, alarmInfo.Content);
                    this.alarmInfos.TryRemove(hashKey, out _);
                }
                else alarmInfo.FiredTimes++;
            }
        }
        else
        {
            //十分钟后，不再报警，就删除掉
            var removeKeys = new List<int>();
            foreach (var alarmInfo in this.alarmInfos)
            {
                if (DateTime.Now.Subtract(alarmInfo.Value.CreatedAt) > TimeSpan.FromMinutes(10))
                    removeKeys.Add(alarmInfo.Key);
            }
            if (removeKeys.Count > 0)
                removeKeys.ForEach(key => this.alarmInfos.TryRemove(key, out _));
        }
        await this.next(context);
    }
    private void Build(LogEntity logEntityInfo, AlarmInfo alarmInfo)
    {
        var body = logEntityInfo.Body;
        if (logEntityInfo.Exception is Exception exception && exception != null)
            body = exception.Message;

        alarmInfo.Header = "告警";
        if (logEntityInfo.LogLevel > (int)LogLevel.Warning)
            alarmInfo.Header = "异常告警";

        var logViewUrl = $"{this.logVisitUrl}thealogs-{logEntityInfo.CreatedAt.Date:yyyyMMdd}/{logEntityInfo.Id}";
        var contentBuilder = new StringBuilder()
            .AppendLine($"[查看]({logViewUrl})  ")
            .AppendLine("**日志信息**  ")
            .AppendLine($"> TraceId：{logEntityInfo.TraceId}  ")
            .AppendLine($"> 应用ID：{logEntityInfo.AppId}  ")
            .AppendLine($"> 用户ID：{logEntityInfo.UserId}  ")
            .AppendLine($"> Headers：{logEntityInfo.Headers}  ")
            .AppendLine($"> 耗  时：{logEntityInfo.Elapsed} ms  ")
            .AppendLine($"> Api地址：{logEntityInfo.ApiUrl}  ")
            .AppendLine($"> 请求参数：{logEntityInfo.Parameters}  ");
        if (logEntityInfo.Exception == null)
            contentBuilder.AppendLine($"> 响应内容：{logEntityInfo.Response}  ");
        contentBuilder.AppendLine($"> 发生时间：{logEntityInfo.CreatedAt:yyyy-MM-dd HH:mm:ss}  ")
            .AppendLine($"> 触发次数：{alarmInfo.FiredTimes}  ").AppendLine();
        if (logEntityInfo.Exception != null)
            contentBuilder.AppendLine("**异常内容**  ").AppendLine($"> {logEntityInfo.Exception}  ");
        else contentBuilder.AppendLine("**详细内容**  ").AppendLine($"> {body}  ");
        alarmInfo.Content = contentBuilder.ToString();
    }
    class AlarmInfo
    {
        public string SenceKey { get; set; }
        public DateTime CreatedAt { get; set; }
        public int FiredTimes { get; set; }
        public string Header { get; set; }
        public string Content { get; set; }
    }
}