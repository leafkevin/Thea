using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Thea.Web;

public static class TheaWebExtensions
{
    public static void AddPassport(this IServiceCollection services)
    {
        services.AddTransient<IPassport, Passport>(provider =>
        {
            var contextAccessor = provider.GetService<IHttpContextAccessor>();
            if (contextAccessor != null)
                return new Passport(contextAccessor.HttpContext.User);
            return null;
        });
    }
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
