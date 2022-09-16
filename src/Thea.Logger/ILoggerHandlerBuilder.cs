using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.Logger;

public delegate Task<bool> LoggerHandlerDelegate(LoggerHandlerContext conext);
public class LoggerHandlerContext
{
    internal LoggerHandlerContext(LogEntity logEntity) => this.LogEntity = logEntity;
    public LogEntity LogEntity { get; private set; }
    public Dictionary<string, object> ContextData { get; set; }
}
public interface ILoggerHandlerBuilder
{
    IServiceProvider ServiceProvider { get; }
    ILoggerHandlerBuilder AddHandler(Func<LoggerHandlerDelegate, LoggerHandlerDelegate> middleware);
    ILoggerHandlerBuilder AddHandler<TMiddleware>(params object[] args);
    LoggerHandlerDelegate Build(LoggerHandlerDelegate first = null);
}
