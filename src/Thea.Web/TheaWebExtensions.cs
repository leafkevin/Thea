using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System;
using System.Security.Claims;
using System.Security.Cryptography;

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
    public static RSA FromPemKey(this RSA rsa, string rsaPublicKey)
    {
        //带有开头结尾注释的那种原装KEY
        rsa.ImportFromPem(rsaPublicKey);
        return rsa;
    }
    public static T ClaimTo<T>(this ClaimsPrincipal user, string type, T defaultValue = default)
    {
        var claim = user.FindFirst(type);
        if (claim == null || string.IsNullOrEmpty(claim.Value))
            return defaultValue;

        var targetType = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType.IsEnum)
            return (T)Enum.Parse(underlyingType, claim.Value);
        return (T)Convert.ChangeType(claim.Value, underlyingType);
    }
    public static IPassport ToPassport(this ClaimsPrincipal user) => new Passport(user);
}
