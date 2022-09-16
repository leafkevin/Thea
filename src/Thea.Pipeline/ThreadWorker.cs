using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.Pipeline;

class ThreadWorker
{
    private readonly Task task;
    private readonly int workerId;
    private readonly PipelineService parent;
    private readonly CancellationTokenSource stopTokenSource = new();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);

    private ConcurrentQueue<WaiterMessage> messageQueue;
    public int WorkerId => this.workerId;
    public ResidentConsumserHandler ResidentHandler { get; set; }

    public ThreadWorker(PipelineService parent, int workerId, IServiceProvider serviceProvider, ConcurrentQueue<WaiterMessage> messageQueue)
    {
        this.parent = parent;
        this.workerId = workerId;
        this.messageQueue = messageQueue;
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<ThreadWorker>();

        this.task = Task.Factory.StartNew(async () =>
        {
            this.readyToStart.WaitOne();

            var lastWatchedAt = DateTime.Now;
            while (!this.stopTokenSource.IsCancellationRequested)
            {
                WaiterMessage message = null;
                try
                {
                    //处理有状态、无状态消息
                    if (this.messageQueue.TryDequeue(out message))
                        await this.HandleMessage(this.workerId, message);

                    //可以处理常驻的事情
                    if (this.ResidentHandler != null)
                        await this.ResidentHandler.Invoke(this.workerId);

                    //处理可延时消息
                    if (this.messageQueue.IsEmpty && !this.TryHandleDdeferredMessage())
                        await Task.Delay(1);
                }
                catch (Exception ex)
                {
                    logger.LogError("ThreadWorker", $"执行异常,message:{message.ToJson()},Detail:{ex}");
                }
            }
        }, this.stopTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Start() => this.readyToStart.Set();
    public void Shutdown()
    {
        this.stopTokenSource.Cancel();
        if (this.task != null)
            this.task.Wait();
        this.stopTokenSource.Dispose();
    }
    public void TryProcessMessage(WaiterMessage message)
        => this.messageQueue.Enqueue(message);


    protected virtual async Task HandleMessage(int workerId, WaiterMessage message)
    {
        if (!this.parent.TryGetExecutor(message.MessageType, out var executor))
            throw new Exception($"没有注册{message.MessageType}处理方法");

        object result = null;
        if (executor.IsMethodAsync)
            result = await executor.ExecuteAsync(message.Target, message.Parameters);
        else result = executor.Execute(message.Target, message.Parameters);
        if (message.Waiter != null)
            message.Waiter.TrySetResult(result.ConvertTo<TheaResponse>());
    }
    protected virtual bool TryHandleDdeferredMessage()
    {
        if (this.parent.TryGetDeferredMessage(out var message))
        {
            this.TryProcessMessage(message);
            return true;
        }
        return false;
    }
}

