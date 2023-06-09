using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Thea.Alarm;

namespace Thea.Logging.Alarm;

public class TheaLogAlarmMiddleware
{
    private readonly Task task;
    private readonly IAlarmService alarmService;
    private readonly string logVisitUrl;
    private readonly LoggerHandlerDelegate next;
    private readonly ConcurrentDictionary<int, AlarmInfo> alarmInfos = new();

    public TheaLogAlarmMiddleware(LoggerHandlerDelegate next, IAlarmService alarmService, IConfiguration configuration, ILogger<TheaLogAlarmMiddleware> logger)
    {
        this.next = next;
        this.alarmService = alarmService;
        this.logVisitUrl = configuration.GetValue<string>("Alarm:LogWebSite");
        if (string.IsNullOrEmpty(this.logVisitUrl))
            throw new ArgumentNullException("appsettings.json not found 'Alarm:LogWebSite' node or value is null.");
    }
    public async Task Invoke(LoggerHandlerContext context)
    {
        var logEntityInfo = context.LogEntity;
        if (logEntityInfo == null) return;
        try
        {
            if (logEntityInfo.LogLevel >= LogLevel.Warning)
            {
                var hashKey = HashCode.Combine(logEntityInfo.AppId, logEntityInfo.UserId, logEntityInfo.ApiUrl, logEntityInfo.Body);
                if (!this.alarmInfos.TryGetValue(hashKey, out var alarmInfo))
                {
                    this.alarmInfos.TryAdd(hashKey, alarmInfo = new AlarmInfo
                    {
                        CreatedAt = DateTime.Now,
                        FiredTimes = 1
                    });
                    this.Build(logEntityInfo, alarmInfo);
                    string senceKey = $"{logEntityInfo.AppId}_{logEntityInfo.UserId}_{logEntityInfo.ApiUrl}_{logEntityInfo.Body}";
                    await this.alarmService.PostAsync(senceKey, alarmInfo.Header, alarmInfo.Content);
                }
                else
                {
                    if (DateTime.Now - alarmInfo.CreatedAt >= TimeSpan.FromMinutes(10))
                    {
                        string senceKey = $"{logEntityInfo.AppId}_{logEntityInfo.UserId}_{logEntityInfo.ApiUrl}_{logEntityInfo.Body}";
                        await this.alarmService.PostAsync(senceKey, alarmInfo.Header, alarmInfo.Content);
                        alarmInfo.CreatedAt = DateTime.Now;
                        alarmInfo.FiredTimes = 1;
                    }
                    this.Build(logEntityInfo, alarmInfo);
                }
            }
            await this.next(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
    private void Build(LogEntity logEntityInfo, AlarmInfo alarmInfo)
    {
        var body = logEntityInfo.Body;
        if (logEntityInfo.Exception != null)
            body = logEntityInfo.Exception.Message;

        alarmInfo.Header = "Warning Alarm Info";
        if (logEntityInfo.LogLevel > LogLevel.Warning)
            alarmInfo.Header = "Exception Alarm Info";

        var logViewUrl = $"{this.logVisitUrl}thealogs-{logEntityInfo.CreatedAt.Date:yyyyMMdd}/{logEntityInfo.Id}";
        var contentBuilder = new StringBuilder()
            .AppendLine($"[click me to view]({logViewUrl})  ")
            .AppendLine("**Request Info**  ")
            .AppendLine($"> App  Id：{logEntityInfo.AppId}  ")
            .AppendLine($"> User Id：{logEntityInfo.UserId}  ")
            .AppendLine($"> Elapsed：{logEntityInfo.Elapsed} ms  ")
            .AppendLine($"> Api Url：{logEntityInfo.ApiUrl}  ")
            .AppendLine($"> Parameters：{logEntityInfo.Parameters}  ")
            .AppendLine($"> Response：{logEntityInfo.Response}  ")
            .AppendLine($"> Created At：{logEntityInfo.CreatedAt:yyyy-MM-dd HH:mm:ss}  ")
            .AppendLine($"> Fired Times：{alarmInfo.FiredTimes}  ").AppendLine();
        if (logEntityInfo.Exception != null)
            contentBuilder.AppendLine("**Exception**  ").AppendLine($"> {logEntityInfo.Exception}  ");
        else contentBuilder.AppendLine("**Body**  ").AppendLine($"> {body}  ");
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