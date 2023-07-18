using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Thea.Logging;

public class TheaLoggerProvider : ILoggerProvider
{
    private ILogger logger;
    private readonly IConfiguration configuration;
    private readonly ILoggerProcessor processor;

    public TheaLoggerProvider(IConfiguration configuration, ILoggerProcessor processor)
    {
        this.configuration = configuration;
        this.processor = processor;
    }
    public ILogger CreateLogger(string categoryName)
    {
        if (this.logger == null)
            this.logger = new TheaLogger(categoryName, this.configuration, this.processor);
        return this.logger;
    }

    public void Dispose()
        => this.logger = null;
}
