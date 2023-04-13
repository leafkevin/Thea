using Microsoft.Extensions.DependencyInjection;
using System;

namespace Thea.JwtTokens;

public static class TheaExtensions
{
    public static void AddIdentityToken(this IServiceCollection services, Action<JwtTokenOptions> optionsInitializer)
    {
        services.Configure(optionsInitializer);
        services.AddSingleton<IJwtTokenService, TheaJwtTokenService>();
    }
}
