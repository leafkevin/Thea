using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Thea.Logging;

public class TheaLoggerProcessor : ILoggerProcessor, IAsyncDisposable
{
    private int built;
    private Task[] tasks = [];
    private LoggerHandlerDelegate next;
    private readonly CancellationTokenSource stopTokenSource = new();
    private Channel<LogEntity> writerChannel = null;
    private Channel<List<LogEntity>> pusherChannel = null;
    private readonly List<Func<LoggerHandlerDelegate, LoggerHandlerDelegate>> components = new();
    private readonly IServiceProvider serviceProvider;
    private readonly IConfiguration configuration;
    private ElasticsearchClient client;
    private readonly bool isEnabled;
    private readonly int batchCount;
    private readonly int pushThreads;

    public TheaLoggerProcessor(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.configuration = serviceProvider.GetService<IConfiguration>();
        this.isEnabled = configuration.GetValue("Logging:IsEnabled", true);
        if (!this.isEnabled) return;

        var batchCount = configuration.GetValue("Logging:PushBatchCount", 100);
        var pushThreads = configuration.GetValue("Logging:PushThreads", 1);
        this.batchCount = Math.Clamp(batchCount, 50, 200);
        this.pushThreads = Math.Clamp(pushThreads, 1, 5);
        this.writerChannel = Channel.CreateBounded<LogEntity>(new BoundedChannelOptions(20000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = this.pushThreads != 2
        });
    }
    public bool TryWrite(LogEntity logEntity)
    {
        if (!this.isEnabled) return false;
        return this.writerChannel.Writer.TryWrite(logEntity);
    }
    public async Task WriteAsync(LogEntity logEntity)
    {
        if (!this.isEnabled) return;
        await this.writerChannel.Writer.WriteAsync(logEntity, this.stopTokenSource.Token);
    }
    public ILoggerProcessor AddHandler(Func<LoggerHandlerDelegate, LoggerHandlerDelegate> middleware)
    {
        components.Add(middleware);
        return this;
    }
    public ILoggerProcessor AddHandler<TMiddleware>(params object[] args)
    {
        var type = typeof(TMiddleware);
        return this.AddHandler(next =>
        {
            var method = type.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
            var ctorArgs = new object[args.Length + 1];
            ctorArgs[0] = next;
            Array.Copy(args, 0, ctorArgs, 1, args.Length);
            var instance = TheaActivator.CreateInstance(this.serviceProvider, type, ctorArgs);
            if (method.GetParameters().Length > 1)
            {
                throw new Exception("Invoke方法只允许有一个RequestHandlerContext类型参数！");
            }
            return (LoggerHandlerDelegate)method.CreateDelegate(typeof(LoggerHandlerDelegate), instance);
        });
    }
    private void CreatePusher()
    {
        if (!this.isEnabled) return;

        var pushUrls = this.configuration.GetSection("Logging:Elastic:Urls").Get<string[]>();
        var user = this.configuration.GetSection("Logging:Elastic:User").Get<string>();
        var password = this.configuration.GetSection("Logging:Elastic:Password").Get<string>();
        var fingerprint = this.configuration.GetSection("Logging:Elastic:Fingerprint").Get<string>();

        if (pushUrls == null || pushUrls.Length <= 0)
            throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:Urls' node or is null.");
        if (string.IsNullOrEmpty(user))
            throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:User' node or is null.");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:Password' node or is null.");
        if (string.IsNullOrEmpty(fingerprint))
            throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:Fingerprint' node or is null.");

        var pool = new StaticNodePool(pushUrls.Select(f => new Uri(f)));
        var settings = new ElasticsearchClientSettings(pool)
            .CertificateFingerprint(fingerprint)
            .Authentication(new BasicAuthentication(user, password))
            .DisableDirectStreaming()
            .RequestTimeout(TimeSpan.FromSeconds(3))
            .MaxRetryTimeout(TimeSpan.FromSeconds(5));
        this.client = new ElasticsearchClient(settings);
        if (pushThreads > 2)
            this.CreateMultiWorkers(this.pushThreads, this.batchCount);
        else this.CreateSingleWorker(this.pushThreads, this.batchCount);
    }
    public void Build(LoggerHandlerDelegate first = null)
    {
        if (Interlocked.Exchange(ref this.built, 1) == 1)
            return;
        try
        {
            LoggerHandlerDelegate app = null;
            if (first == null) app = context => Task.FromResult(true);
            else app = context => first(context);
            this.components.Reverse();
            foreach (var component in this.components)
            {
                app = component(app);
            }
            this.next = app;
            this.CreatePusher();
        }
        catch
        {
            Interlocked.Exchange(ref this.built, 0);
            throw;
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (!this.isEnabled)
        {
            this.stopTokenSource.Dispose();
            return;
        }
        this.writerChannel.Writer.TryComplete();
        this.pusherChannel?.Writer.TryComplete();
        this.stopTokenSource.Cancel();
        if (this.tasks is { Length: > 0 })
        {
            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }
        this.stopTokenSource.Dispose();
    }
    private void CreateSingleWorker(int pushThreads, int batchCount)
    {
        this.tasks = new Task[pushThreads];
        for (int i = 0; i < pushThreads; i++)
        {
            this.tasks[i] = Task.Run(async () =>
            {
                var pushInterval = TimeSpan.FromSeconds(5);
                var lastPushedTime = DateTime.UtcNow;
                var logEntities = new List<LogEntity>();
                while (!this.stopTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        while (logEntities.Count < batchCount)
                        {
                            if (DateTime.UtcNow.Subtract(lastPushedTime) > TimeSpan.FromSeconds(5))
                                break;
                            if (this.writerChannel.Reader.TryRead(out var logEntity))
                                logEntities.Add(logEntity);
                            else
                            {
                                var remaining = pushInterval - (DateTime.UtcNow - lastPushedTime);
                                if (remaining <= TimeSpan.Zero) break;
                                if (!await this.ReadLogEntity(logEntities, remaining))
                                    break;
                            }
                        }
                        if (logEntities.Count > 0)
                        {
                            try
                            {
                                await this.NextInvoke(logEntities);
                                await this.SendToAsync(logEntities);
                            }
                            finally { logEntities.Clear(); }
                        }
                        lastPushedTime = DateTime.UtcNow;
                    }
                    catch (OperationCanceledException)
                        when (this.stopTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        logEntities.Clear();
                    }
                }
            }, this.stopTokenSource.Token);
        }
    }
    private void CreateMultiWorkers(int pushThreads, int batchCount)
    {
        var capacity = Math.Max(200 * 1024 / batchCount / 5, pushThreads * 20);
        this.pusherChannel = Channel.CreateBounded<List<LogEntity>>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });
        this.tasks = new Task[pushThreads];
        this.tasks[0] = Task.Run(async () =>
        {
            var flushInterval = TimeSpan.FromSeconds(5);
            var lastReadTime = DateTime.UtcNow;
            while (!this.stopTokenSource.IsCancellationRequested)
            {
                try
                {
                    var logEntities = new List<LogEntity>();
                    while (logEntities.Count < batchCount)
                    {
                        if (DateTime.UtcNow.Subtract(lastReadTime) >= flushInterval)
                            break;
                        if (this.writerChannel.Reader.TryRead(out var logEntity))
                            logEntities.Add(logEntity);
                        else
                        {
                            var remaining = flushInterval - (DateTime.UtcNow - lastReadTime);
                            if (remaining <= TimeSpan.Zero) break;
                            if (!await this.ReadLogEntity(logEntities, remaining))
                                break;
                        }
                    }
                    if (logEntities.Count > 0)
                        await this.pusherChannel.Writer.WriteAsync(logEntities, this.stopTokenSource.Token);
                    lastReadTime = DateTime.UtcNow;
                }
                catch (OperationCanceledException)
                    when (this.stopTokenSource.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }, this.stopTokenSource.Token);
        for (int i = 1; i < pushThreads; i++)
        {
            this.tasks[i] = Task.Run(async () =>
            {
                while (!this.stopTokenSource.IsCancellationRequested)
                {
                    try { await this.ExecuteAsync(); }
                    catch (OperationCanceledException)
                        when (this.stopTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }
            }, this.stopTokenSource.Token);
        }
    }
    private async Task<bool> ReadLogEntity(List<LogEntity> logEntities, TimeSpan timeout)
    {
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(this.stopTokenSource.Token);
            timeoutSource.CancelAfter(timeout);
            if (await this.writerChannel.Reader.WaitToReadAsync(timeoutSource.Token)
                && this.writerChannel.Reader.TryRead(out var logEntityInfo))
            {
                logEntities.Add(logEntityInfo);
                return true;
            }
        }
        catch (OperationCanceledException) when (!this.stopTokenSource.IsCancellationRequested)
        {
            // flush timeout
        }
        return false;
    }
    private async Task ExecuteAsync()
    {
        while (this.pusherChannel.Reader.TryRead(out var logEntities))
        {
            try
            {
                await this.NextInvoke(logEntities);
                await this.SendToAsync(logEntities);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
        if (!await this.pusherChannel.Reader.WaitToReadAsync(this.stopTokenSource.Token))
            return;
    }
    private async Task NextInvoke(List<LogEntity> logEntities)
    {
        if (this.next == null) return;
        foreach (var logEntity in logEntities)
        {
            try
            {
                await this.next.Invoke(logEntity);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoggerHandler execute error,{ex}");
            }
        }
    }
    private async Task SendToAsync(List<LogEntity> logs)
    {
        if (!this.isEnabled)
        {
            logs.ForEach(f => Console.WriteLine(f.ToJson()));
            return;
        }
        if (logs.Count <= 0) return;
        foreach (var logEntity in logs)
        {
            if (logEntity.Exception == null) continue;
            logEntity.Exception = logEntity.Exception.ToString();
        }
        var esIndex = $"thealogs-{DateTime.Now:yyyyMMdd}";
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(this.stopTokenSource.Token);
        tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var result = await this.client.IndexManyAsync(logs, esIndex, tokenSource.Token);
            if (!result.IsValidResponse)
            {
                Console.WriteLine(result.DebugInformation);
                Console.WriteLine(result.ElasticsearchServerError);
            }
        }
        catch (OperationCanceledException)
            when (tokenSource.IsCancellationRequested)
        {
            // timeout计数
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
        }
    }
}