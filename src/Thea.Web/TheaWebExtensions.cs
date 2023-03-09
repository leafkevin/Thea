using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Globalization;
using System.Security.Claims;
using System;

namespace Thea.Web;

public static class TheaWebExtensions
{
    public static void AddPassport(this IServiceCollection services)
    {
        services.AddTransient<IPassport>(provider =>
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

    internal static T ClaimTo<T>(this ClaimsPrincipal user, string type, T defaultValue = default)
    {
        var claim = user.FindFirst(type);
        if (claim == null || string.IsNullOrEmpty(claim.Value))
            return defaultValue;

        var targetType = typeof(T);
        if (targetType.IsEnum)
            return (T)Enum.Parse(typeof(T), claim.Value);
        if (claim.Value is IConvertible convertible)
            return (T)convertible.ToType(targetType, CultureInfo.CurrentCulture);
        return defaultValue;
    }
}
