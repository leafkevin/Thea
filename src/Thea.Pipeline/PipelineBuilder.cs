using System;

namespace Thea.Pipeline;

public class PipelineBuilder
{
    private readonly PipelineService service;
    public PipelineBuilder(PipelineService service) => this.service = service;

    public PipelineBuilder Register(int workerCount)
    {
        this.service.ThreadCount = workerCount;
        return this;
    }
    public PipelineBuilder AddHandler(int messageType, bool isStatefullMessage, Type handlerType, string methodName)
    {
        this.service.RegisterHandler(messageType, isStatefullMessage, handlerType, methodName);
        return this;
    }
    public PipelineBuilder AddHandler<THandler>(int messageType, bool isStatefullMessage, string methodName)
    {
        var handlerType = typeof(THandler);
        this.service.RegisterHandler(messageType, isStatefullMessage, handlerType, methodName);
        return this;
    }
    public PipelineBuilder AddResidentHandler(ResidentConsumserHandler handler)
    {
        this.service.ResidentHandler = handler;
        return this;
    }
}
