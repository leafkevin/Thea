using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace Thea.Logging;

public class TheaLoggerProvider : ILoggerProvider
{
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment hostEnvironment;
    private readonly ILoggerProcessor processor;
    private readonly ConcurrentDictionary<string, ILogger> loggers = new(StringComparer.OrdinalIgnoreCase);

    public TheaLoggerProvider(IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerProcessor processor)
    {
        this.configuration = configuration;
        this.hostEnvironment = hostEnvironment;
        this.processor = processor;
    }
    public ILogger CreateLogger(string categoryName)
    {
        //就创建两个日志器实例
        if (categoryName.StartsWith("Microsoft.Hosting")
            || categoryName.StartsWith("Microsoft.Extensions.Hosting")
            || categoryName.StartsWith("Microsoft.AspNetCore"))
            categoryName = "Microsoft.AspNetCore";
        else categoryName = "Thea";
        return this.loggers.GetOrAdd(categoryName, f => new TheaLogger(categoryName, this.configuration, this.hostEnvironment, this.processor));
    }
    public void Dispose() => this.loggers.Clear();
}
