using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thea.Alarm;

namespace Thea.Logging.Alarm;

public class TheaLoggerAlarmMiddleware
{
    private readonly Task task;
    private readonly IAlarmService alarmService;
    private readonly string logVisitUrl;
    private readonly LoggerHandlerDelegate next;
    private readonly ConcurrentQueue<AlarmInfo> messageQueue = new();
    private readonly ConcurrentDictionary<int, AlarmFiredInfo> firedInfos = new();
    private readonly CancellationTokenSource stopTokenSource = new();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);

    public TheaLoggerAlarmMiddleware(LoggerHandlerDelegate next, IAlarmService alarmService, IConfiguration configuration, ILogger<TheaLoggerAlarmMiddleware> logger)
    {
        this.next = next;
        this.alarmService = alarmService;
        this.logVisitUrl = configuration.GetValue<string>("Alarm:LogWebSite");
        if (string.IsNullOrEmpty(this.logVisitUrl))
            throw new ArgumentNullException("appsettings.json not found 'Alarm:LogWebSite' node or value is null.");

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();
            while (!this.stopTokenSource.IsCancellationRequested)
            {
                try
                {
                    if (this.messageQueue.TryPeek(out var alarmInfo))
                    {
                        if (!this.firedInfos.TryGetValue(alarmInfo.HashKey, out var firedInfo))
                        {
                            this.messageQueue.TryDequeue(out _);
                            continue;
                        }

                        //十分钟后再报一次
                        if (DateTime.Now - firedInfo.CreatedAt > TimeSpan.FromMinutes(10))
                        {
                            var title = new StringBuilder(alarmInfo.Header)
                               .AppendLine($"> Fired Times：{firedInfo.FiredTimes}  ").ToString();
                            await this.alarmService.PostAsync(alarmInfo.SenceKey, title, alarmInfo.Content);
                            this.messageQueue.TryDequeue(out _);
                        }
                    }
                    else Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"execute post alarm error, exception:{ex}");
                }
            }
        }, stopTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Start() => this.readyToStart.Set();
    public void Shutdown()
    {
        this.stopTokenSource.Cancel();
        this.task?.Wait();
        this.readyToStart.Dispose();
        this.stopTokenSource.Dispose();
    }
    public async Task<bool> Invoke(LoggerHandlerContext context)
    {
        var logEntityInfo = context.LogEntity;
        if (logEntityInfo.LogLevel >= LogLevel.Warning)
        {
            var hashKey = HashCode.Combine(logEntityInfo.AppId, logEntityInfo.UserId, logEntityInfo.ApiUrl, logEntityInfo.Body);
            if (!this.firedInfos.TryGetValue(hashKey, out var firedInfo))
            {
                this.firedInfos.TryAdd(hashKey, firedInfo = new AlarmFiredInfo
                {
                    CreatedAt = firedInfo.CreatedAt,
                    FiredTimes = 1
                });
                var senceKey = $"{logEntityInfo.AppId}_{logEntityInfo.UserId}_{logEntityInfo.ApiUrl}_{logEntityInfo.Body}";
                var titleBuilder = new StringBuilder()
                    .AppendLine($"#### {logEntityInfo.Body}  ")
                    .AppendLine($"> App Id：{logEntityInfo.AppId}  User Id：{logEntityInfo.UserId}  ")
                    .AppendLine($"> ApiUrl：{logEntityInfo.ApiUrl}  ")
                    .AppendLine($"> Created At：{firedInfo.CreatedAt:yyyy-MM-dd HH:mm:ss}  ");
                var header = titleBuilder.ToString();
                var title = titleBuilder.AppendLine($"> Fired Times：{firedInfo.FiredTimes}  ").ToString();

                var logViewUrl = $"{this.logVisitUrl}logs-{logEntityInfo.CreatedAt.Date:yyyyMMdd}/{logEntityInfo.Id}";
                var contentBuilder = new StringBuilder()
                    .AppendLine($"[click me to view]({logViewUrl})  ")
                    .AppendLine("**Request Info**  ")
                    .AppendLine($"> Elapsed：{logEntityInfo.Elapsed}ms  ")
                    .AppendLine($"> Api Url：{logEntityInfo.ApiUrl}  ")
                    .AppendLine($"> Parameters：{logEntityInfo.Parameters}  ");
                if (logEntityInfo.Exception != null)
                    contentBuilder.AppendLine().AppendLine("**Exception**  ").AppendLine($"> {logEntityInfo.Exception}  ");
                else
                    contentBuilder.AppendLine().AppendLine("**Body**  ").AppendLine($"> {logEntityInfo.Body}  ");
                var content = contentBuilder.ToString();

                await this.alarmService.PostAsync(senceKey, title, content);
                this.messageQueue.Enqueue(new AlarmInfo
                {
                    HashKey = hashKey,
                    SenceKey = senceKey,
                    Header = header,
                    Content = content
                });
            }
        }
        return await this.next(context);
    }
}
class AlarmFiredInfo
{
    public DateTime CreatedAt { get; set; }
    public int FiredTimes { get; set; }
}
class AlarmInfo
{
    public int HashKey { get; set; }
    public string SenceKey { get; set; }
    public string Header { get; set; }
    public string Content { get; set; }
}