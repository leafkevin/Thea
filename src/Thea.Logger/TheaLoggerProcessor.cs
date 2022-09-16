using Elasticsearch.Net;
using Microsoft.Extensions.Configuration;
using Nest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.Logger;

public class TheaLoggerProcessor
{
    private readonly Task task;
    private IElasticClient _client;
    private LoggerHandlerDelegate next;
    private readonly ILoggerHandlerBuilder builder;
    private readonly CancellationTokenSource stopTokenSource = new CancellationTokenSource();
    private readonly ConcurrentQueue<LogEntity> messageQueue = new ConcurrentQueue<LogEntity>();
    private readonly int pushBatchCount = 50;
    private DateTime lastUpdateTime = DateTime.Now;
    public TheaLoggerProcessor(IConfiguration configuration, ILoggerHandlerBuilder builder)
    {
        this.Initialize(configuration);
        this.builder = builder;
        this.pushBatchCount = configuration.GetValue<int>("Logging:PushBatchCount", 50);
        this.task = Task.Factory.StartNew(async () =>
        {
            var logList = new List<LogEntity>();
            while (!this.stopTokenSource.IsCancellationRequested)
            {
                try
                {
                    if (logList.Count < this.pushBatchCount && this.messageQueue.TryDequeue(out var logEntityInfo))
                    {
                        try
                        {
                            if (this.next != null && !await this.next.Invoke(new LoggerHandlerContext(logEntityInfo)))
                                continue;
                        }
                        catch (Exception ex)
                        {
                            logList.Add(new LogEntity
                            {
                                Id = ObjectId.NewId(),
                                LogLevel = Microsoft.Extensions.Logging.LogLevel.Error,
                                AppId = "Thea.Logger",
                                Exception = ex,
                                Body = $"LoggerHandler Error,LogEntity:{logEntityInfo.ToJson()},Detail:{ex.Message}",
                                CreatedAt = DateTime.Now,
                                LogTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                                Source = "Thea.Logger.Handler",
                                ThreadId = Thread.CurrentThread.ManagedThreadId,
                                TraceId = logEntityInfo.TraceId,
                                Tag = logEntityInfo.Tag
                            });
                        }
                        logList.Add(logEntityInfo);
                    }
                    if (logList.Count >= this.pushBatchCount || DateTime.Now - this.lastUpdateTime > TimeSpan.FromSeconds(5))
                    {
                        //如果推送失败，就5秒后再重试，直到推送成功
                        this.lastUpdateTime = DateTime.Now;
                        if (!this.SendToAsync(logList))
                            continue;
                        logList.Clear();
                    }
                    else Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }, this.stopTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Build() => this.next = this.builder.Build();
    public void TryPush(LogEntity logEntity) => this.messageQueue.Enqueue(logEntity);
    public void Dispose()
    {
        this.stopTokenSource.Cancel();
        if (this.task != null) this.task.Wait();
        this.stopTokenSource.Dispose();
    }
    private void Initialize(IConfiguration configuration)
    {
        var pushUrls = configuration.GetSection("Logging:PushUrls").Get<string[]>();
        if (pushUrls.Length <= 0)
        {
            throw new ArgumentNullException(
                "In appsettings.json file not found 'Logging:PushUrls' node or is null.");
        }
        var pool = new StaticConnectionPool(pushUrls.Select(f => new Uri(f)));
        var connectionSettings = new ConnectionSettings(pool);
        _client = new ElasticClient(connectionSettings);
    }
    private bool SendToAsync(List<LogEntity> logs)
    {
        try
        {
            if (logs.Count > 0)
            {
                Console.WriteLine(logs.ToJson());
                return true;
                //var esIndex = $"logs-{DateTime.Now:yyyyMMdd}";
                //var result = await this._client.IndexManyAsync(logs, esIndex);
                //if (!result.IsValid) Console.WriteLine(result.DebugInformation);
                //return result.IsValid;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
        return true;
    }
}
