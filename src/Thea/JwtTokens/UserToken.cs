using System;

namespace Thea.JwtTokens;

public class UserToken
{
    public string Id { get; set; }
    public string Account { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string Mobile { get; set; }
    public string ClientId { get; set; }
    public string Audience { get; set; }
    public string Scope { get; set; }
    public string Role { get; set; }
    public string TenantType { get; set; }
    public int? TenantId { get; set; }
    public TimeSpan? LifeTime { get; set; }
}
