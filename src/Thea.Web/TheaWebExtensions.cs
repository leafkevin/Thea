using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Thea.Web;

public static class TheaWebExtensions
{
    public static IServiceCollection AddTheaWeb(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.AddTransient<HttpMessageHandlerBuilder, TheaHttpMessageHandlerBuilder>();
        services.AddTransient<TheaHttpMessageHandler>();
        return services;
    }
    public static IApplicationBuilder UseTheaWeb(this IApplicationBuilder app)
        => app.UseMiddleware<TheaWebMiddleware>();
}
