using System;
using System.Threading.Tasks;

namespace Thea.Logging;

public delegate Task LoggerHandlerDelegate(LogEntity logEntity);
public interface ILoggerProcessor
{
    Task ExecuteAsync(LogEntity logEntity);
    ILoggerProcessor AddHandler(Func<LoggerHandlerDelegate, LoggerHandlerDelegate> middleware);
    ILoggerProcessor AddHandler<TMiddleware>(params object[] args);
    void Build(LoggerHandlerDelegate first = null);
}