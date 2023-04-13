using System;

namespace Thea.JwtTokens;

public class JwtTokenOptions
{
    public string IssuerUri { get; set; }
    public TimeSpan LifeTime { get; set; } = TimeSpan.FromMinutes(5);
    /// <summary>
    /// 用于JWT加密的私钥Key
    /// </summary>
    public string SecretKey { get; set; }
}
