using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Thea.Logging;

public class TheaLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, ILogger> loggers = new();
    private readonly IConfiguration configuration;
    private readonly ILoggerProcessor processor;

    public TheaLoggerProvider(IConfiguration configuration, ILoggerProcessor processor)
    {
        this.configuration = configuration;
        this.processor = processor;
    }
    public ILogger CreateLogger(string categoryName)
    {
        if (this.loggers.TryGetValue(categoryName, out var logger))
            return logger;
        this.loggers.TryAdd(categoryName, logger = new TheaLogger(categoryName, this.configuration, this.processor));
        return logger;
    }

    public void Dispose()
    {
        var removeList = this.loggers.Keys.ToList();
        foreach (var key in removeList)
        {
            if (!this.loggers.TryRemove(key, out var logger))
                continue;
            if (logger is IDisposable disposableObj)
                disposableObj.Dispose();
        }
        this.loggers.Clear();
    }
}
