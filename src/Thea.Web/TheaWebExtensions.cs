using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Thea.Web;

public static class TheaWebExtensions
{
    //public static IServiceCollection AddTheaAuthentication(this IServiceCollection services, IConfiguration configuration)
    //{
    //    if (configuration.GetValue<bool>("Authentication:IsEnabled", false))
    //    {
    //        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    //            .AddIdentityServerAuthentication(options =>
    //            {
    //                options.Authority = configuration["Authentication:Url"];
    //                options.RequireHttpsMetadata = false;
    //                options.ApiName = configuration["Authentication:ApiResource"];
    //            });
    //        services.AddTransient<IPassport, Passport>(TheaPassportFactory);
    //    }
    //    return services;
    //} 
    public static IServiceCollection AddThea(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.AddTransient<HttpMessageHandlerBuilder, TheaHttpMessageHandlerBuilder>();
        services.AddTransient<TheaHttpMessageHandler>();
        return services;
    }
    public static IApplicationBuilder UseThea(this IApplicationBuilder app)
        => app.UseMiddleware<TheaMiddleware>();
    //private static Passport TheaPassportFactory(IServiceProvider provider)
    //{
    //    var httpAccessor = provider.GetService<IHttpContextAccessor>();
    //    if (httpAccessor == null || httpAccessor.HttpContext.User == null)
    //        return null;
    //    return Passport.ParseFrom(httpAccessor.HttpContext?.User);
    //}
}
