using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace Thea.Pipeline;

public static class PipelineExtensions
{
    public static void AddPipeline(this IServiceCollection services)
        => services.AddSingleton<PipelineService>(f => new PipelineService(f));
    public static IApplicationBuilder UsePipeline(this IApplicationBuilder app, Action<PipelineBuilder> builderInitializer)
    {        
        var service = app.ApplicationServices.GetService<PipelineService>();
        var builder = new PipelineBuilder(service);
        builderInitializer.Invoke(builder);
        var lifetime = app.ApplicationServices.GetService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() => service.Shutdown());
        service.Start();
        return app;
    }
}
