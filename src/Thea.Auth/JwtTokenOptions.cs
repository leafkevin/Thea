using System;

namespace Thea.Auth;

public class JwtTokenOptions
{
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public TimeSpan LifeTime { get; set; } = TimeSpan.FromMinutes(5);
    public string PrivateSecretKey { get; set; }
    public string PublicSecretKey { get; set; }
}
