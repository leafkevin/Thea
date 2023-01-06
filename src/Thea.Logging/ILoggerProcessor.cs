using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.Logging;

public delegate Task<bool> LoggerHandlerDelegate(LoggerHandlerContext conext);
public class LoggerHandlerContext
{
    internal LoggerHandlerContext(LogEntity logEntity) => this.LogEntity = logEntity;
    public LogEntity LogEntity { get; private set; }
    public Dictionary<string, object> ContextData { get; set; }
}
public interface ILoggerProcessor
{
    void Execute(LogEntity logEntity);
    ILoggerProcessor AddHandler(Func<LoggerHandlerDelegate, LoggerHandlerDelegate> middleware);
    ILoggerProcessor AddHandler<TMiddleware>(params object[] args);
    void Build(LoggerHandlerDelegate first = null);
}
