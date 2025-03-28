using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Thea.Logging;

public class TheaLoggerProvider : ILoggerProvider
{
    private ILogger logger;
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment hostEnvironment;
    private readonly ILoggerProcessor processor;

    public TheaLoggerProvider(IConfiguration configuration, IHostEnvironment hostEnvironment, ILoggerProcessor processor)
    {
        this.configuration = configuration;
        this.hostEnvironment = hostEnvironment;
        this.processor = processor;
    }
    public ILogger CreateLogger(string categoryName)
    {
        if (this.logger == null)
            this.logger = new TheaLogger(categoryName, this.configuration, this.hostEnvironment, this.processor);
        return this.logger;
    }

    public void Dispose()
        => this.logger = null;
}
