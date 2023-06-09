using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Thea.Orm;

namespace Thea.Logging.Template;

public static class TheaTemplateLogExtensions
{
    public static IApplicationBuilder UseTheaTemplateLog<TOrmProvider>(this IApplicationBuilder app)
    {
        var loggerProcessor = app.ApplicationServices.GetService<ILoggerProcessor>();
        var dbFactory = app.ApplicationServices.GetService<IOrmDbFactory>();
        var ormProviderType = typeof(TOrmProvider);
        dbFactory.Configure(ormProviderType, new ModelConfiguration());
        dbFactory.Build(ormProviderType);
        loggerProcessor.AddHandler<TheaTemplateLogMiddleware>();
        loggerProcessor.Build();
        return app;
    }
}