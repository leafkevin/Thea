using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Thea.Job;
using Thea.Orm;

namespace Thea.MessageDriven;

public class MessageDrivenBuilder
{
    private readonly MessageDrivenService service;
    private readonly IOrmDbFactory dbFactory;
    private readonly IServiceProvider serviceProvider;

    internal MessageDrivenBuilder(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.service = serviceProvider.GetService<IMessageDriven>() as MessageDrivenService;
        this.dbFactory = serviceProvider.GetService<IOrmDbFactory>();
    }

    public MessageDrivenBuilder Create(string dbKey, string hostName)
    {
        this.service.DbKey = dbKey;
        this.service.HostName = hostName;
        return this;
    }
    public MessageDrivenBuilder AddProducer(string clusterId, bool isUseRpc = false)
    {
        this.service.AddProducer(clusterId, isUseRpc);
        return this;
    }
    public MessageDrivenBuilder AddConsumer<TConsumer>(string clusterId, Func<TConsumer, Delegate> consumerHandlerSelector)
    {
        var consumer = serviceProvider.GetService<TConsumer>();
        var methodInfo = consumerHandlerSelector.Invoke(consumer).Method;
        var executor = ObjectMethodExecutor.Create(methodInfo, typeof(TConsumer).GetTypeInfo());
        var parameterType = methodInfo.GetParameters().First().ParameterType;
        this.service.AddConsumer(clusterId, consumer, parameterType, executor);
        return this;
    }
    public MessageDrivenBuilder Configure<TOrmProvider>(IModelConfiguration configuration) where TOrmProvider : class, IOrmProvider, new()
    {
        this.dbFactory.Configure(typeof(TOrmProvider), configuration);
        return this;
    }
    public MessageDrivenBuilder Configure<TOrmProvider, TModelConfiguration>()
        where TOrmProvider : class, IOrmProvider, new()
        where TModelConfiguration : class, IModelConfiguration, new()
    {
        this.dbFactory.Configure(typeof(TOrmProvider), new TModelConfiguration());
        return this;
    }
}