using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Thea.Pipeline;

public class PipelineService
{
    private readonly IServiceProvider serviceProvider;
    private readonly Dictionary<int, ObjectMethodExecutor> executors = new();
    private readonly Dictionary<int, ThreadWorker> workers = new();
    private readonly Dictionary<int, bool> categories = new();

    private ConcurrentQueue<WaiterMessage> statelessQueue = null;
    private bool hasStatelessMessage = false;
    private bool hasStatefullMessage = false;

    public int ThreadCount { get; set; }
    public ResidentConsumserHandler ResidentHandler { get; set; }

    internal PipelineService(IServiceProvider serviceProvider)
        => this.serviceProvider = serviceProvider;

    /// <summary>
    /// 处理消息，根据注册的消息类别，自动处理
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public async Task<TheaResponse> ProcessMessage(WaiterMessage message)
    {
        if (message == null)
            throw new ArgumentNullException("message");

        var isStatefullMessage = this.categories[message.MessageType];
        if (isStatefullMessage)
        {
            if (string.IsNullOrEmpty(message.RoutingKey))
                throw new Exception($"消息类型{message.MessageType}是有状态消息，需要指定路由,routingKey不能为空");

            //使用Farm哈希，google发明的，目前是最快最散列的哈希算法
            var index = (int)(Farmhash.Hash32(message.RoutingKey) % this.ThreadCount);
            message.Waiter = new TaskCompletionSource<TheaResponse>();
            this.workers[index].TryProcessMessage(message);
            return await message.Waiter.Task;
        }
        else
        {
            //有状态消息和无状态消息并存时，无状态消息延时处理，无需等待
            if (!this.hasStatefullMessage)
                message.Waiter = new TaskCompletionSource<TheaResponse>();
            this.statelessQueue.Enqueue(message);
            if (!this.hasStatefullMessage)
                return await message.Waiter.Task;
        }
        return TheaResponse.Success;
    }

    public void Start()
    {
        ConcurrentQueue<WaiterMessage> pipeline = null;
        if (this.hasStatelessMessage)
            this.statelessQueue = new ConcurrentQueue<WaiterMessage>();

        for (int i = 0; i < this.ThreadCount; i++)
        {
            if (this.hasStatelessMessage)
                pipeline = new ConcurrentQueue<WaiterMessage>();
            else pipeline = this.statelessQueue;

            var worker = new ThreadWorker(this, i, serviceProvider, pipeline);
            this.workers.Add(i, worker);
            worker.Start();
        }
    }
    public void Shutdown()
    {
        for (int i = 0; i < this.ThreadCount; i++)
        {
            this.workers[i].Shutdown();
        }
    }

    public void RegisterHandler(int messageType, bool isStatefullMessage, Type handlerType, string methodName)
    {
        var methodInfo = handlerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var executor = ObjectMethodExecutor.Create(methodInfo, handlerType.GetTypeInfo());
        this.executors.Add(messageType, executor);
        this.categories.Add(messageType, isStatefullMessage);

        if (isStatefullMessage)
            this.hasStatefullMessage = true;
        else this.hasStatelessMessage = true;
    }
    public void RegisterHandler<THandler>(int messageType, bool isStatefullMessage, string methodName)
    {
        var handlerType = typeof(THandler);
        this.RegisterHandler(messageType, isStatefullMessage, handlerType, methodName);
    }

    internal bool TryGetExecutor(int messageType, out ObjectMethodExecutor executor)
        => this.executors.TryGetValue(messageType, out executor);
    internal bool TryGetDeferredMessage(out WaiterMessage message)
    {
        if (!this.hasStatelessMessage)
        {
            message = null;
            return false;
        }
        return this.statelessQueue.TryDequeue(out message);
    }
}