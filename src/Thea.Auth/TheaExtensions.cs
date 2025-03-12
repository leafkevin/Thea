using Duende.IdentityModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Security.Cryptography;
using Trolley;

namespace Thea.Auth;

public static class TheaExtensions
{
    public static void AddTheaAuthentication(this IServiceCollection services, Action<JwtTokenOptions> optionsInitializer, bool isUseRoleResourceAuth = false)
    {
        var tokenOptions = new JwtTokenOptions();
        optionsInitializer?.Invoke(tokenOptions);
        services.Configure(optionsInitializer);
        services.AddSingleton<IJwtTokenService, TheaJwtTokenService>();

        services.AddAuthentication("Bearer")
            .AddJwtBearer("Bearer", options =>
            {
                options.IncludeErrorDetails = true;
                var rsaPublicKey = tokenOptions.PublicSecretKey;
                var rsa = RSA.Create();
                rsa.ImportFromPem(rsaPublicKey);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = JwtClaimTypes.Name,
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = tokenOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudiences = new string[] { tokenOptions.Audience },
                    IssuerSigningKey = new RsaSecurityKey(rsa)
                };
                options.SaveToken = true;
            });
        if (isUseRoleResourceAuth)
            services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
    }
    public static IApplicationBuilder UseRoleResourceAuth(this IApplicationBuilder app, IConfiguration configuration)
    {
        var dbKey = configuration.GetValue<string>("Authorization:DbKey");
        var dbFactory = app.ApplicationServices.GetService<IOrmDbFactory>();
        if (dbFactory == null) throw new Exception("请先注册Trolley组件");
        dbFactory.Configure<ModelConfiguration>(dbKey);
        dbFactory.Build();
        return app;
    }
}
