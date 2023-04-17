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
    public TimeSpan? LifeTime { get; set; }
    public string Country { get; set; }
    public string State { get; set; }
    public string City { get; set; }
    public int? Status { get; set; }
}
