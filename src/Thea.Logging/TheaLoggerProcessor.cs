using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.Logging
{
    public class TheaLoggerProcessor : ILoggerProcessor
    {
        private readonly Task task;
        private LoggerHandlerDelegate next;
        private readonly CancellationTokenSource stopTokenSource = new();
        private readonly ConcurrentQueue<LogEntity> messageQueue = new();
        private readonly List<Func<LoggerHandlerDelegate, LoggerHandlerDelegate>> components = new();
        private readonly IServiceProvider serviceProvider;
        private readonly ElasticsearchClient client;
        private DateTime lastPushedTime = DateTime.Now;

        public TheaLoggerProcessor(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            var configuration = serviceProvider.GetService<IConfiguration>();

            var pushUrls = configuration.GetSection("Logging:Elastic:Urls").Get<string[]>();
            var user = configuration.GetSection("Logging:Elastic:User").Get<string>();
            var password = configuration.GetSection("Logging:Elastic:Password").Get<string>();
            var fingerprint = configuration.GetSection("Logging:Elastic:Fingerprint").Get<string>();

            if (pushUrls == null || pushUrls.Length <= 0)
                throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:Urls' node or is null.");
            if (string.IsNullOrEmpty(user))
                throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:User' node or is null.");
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:Password' node or is null.");
            if (string.IsNullOrEmpty(fingerprint))
                throw new ArgumentNullException("In appsettings.json file not found 'Logging:Elastic:Fingerprint' node or is null.");

            var pool = new StaticNodePool(pushUrls.Select(f => new Uri(f)));
            var settings = new ElasticsearchClientSettings(new Uri(pushUrls[0]))
                .CertificateFingerprint(fingerprint)
                .Authentication(new BasicAuthentication(user, password))
                .DisableDirectStreaming();
            this.client = new ElasticsearchClient(settings);
            var batchCount = configuration.GetValue("Logging:PushBatchCount", 100);

            this.task = Task.Factory.StartNew(async () =>
            {
                var logEntities = new List<LogEntity>();
                while (!this.stopTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        if (this.messageQueue.TryDequeue(out var logEntityInfo))
                            logEntities.Add(logEntityInfo);

                        if (logEntities.Count >= batchCount
                            || DateTime.Now.Subtract(this.lastPushedTime) > TimeSpan.FromSeconds(10))
                        {
                            if (this.next != null)
                            {
                                if (logEntities.Count > 0)
                                {
                                    foreach (var logEntity in logEntities)
                                    {
                                        try
                                        {
                                            var context = new LoggerHandlerContext(logEntity);
                                            await this.next.Invoke(context);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"LoggerHandler execute error,{ex}");
                                            logEntities.Remove(logEntity);
                                        }
                                    }
                                }
                                else await this.next.Invoke(LoggerHandlerContext.Instance);
                            }
                            await this.SendToAsync(logEntities);
                            logEntities.Clear();
                        }
                        if (this.messageQueue.Count <= 0)
                            Thread.Sleep(100);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                        logEntities.Clear();
                    }
                }
            }, this.stopTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        public void Execute(LogEntity logEntity) => this.messageQueue.Enqueue(logEntity);
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
        public void Build(LoggerHandlerDelegate first = null)
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
        }
        public void Dispose()
        {
            this.stopTokenSource.Cancel();
            if (this.task != null) this.task.Wait();
            this.stopTokenSource.Dispose();
        }
        private async Task SendToAsync(List<LogEntity> logs)
        {
            if (logs.Count <= 0) return;
            var esIndex = $"thealogs-{DateTime.Now:yyyyMMdd}";
            foreach (var logEntity in logs)
            {
                if (logEntity.Exception == null) continue;
                logEntity.Exception = logEntity.Exception.ToString();
            }
            var result = await this.client.IndexManyAsync(logs, esIndex);
            if (!result.IsValidResponse)
            {
                Console.WriteLine(result.DebugInformation);
                Console.WriteLine(result.ElasticsearchServerError);
            }
        }
    }
}
