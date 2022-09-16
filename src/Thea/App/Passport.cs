using Microsoft.IdentityModel.JsonWebTokens;
using System;
using System.Globalization;
using System.Security.Claims;

namespace Thea;

public class Passport : IPassport
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public int Id { get; private set; }
    /// <summary>
    /// 用户账号
    /// </summary>
    public string UserAccount { get; private set; }
    /// <summary>
    /// 用户姓名
    /// </summary>
    public string Name { get; private set; }
    /// <summary>
    /// 租户类型
    /// </summary>
    public int TenantType { get; set; }
    /// <summary>
    /// 租户ID
    /// </summary>
    public int TenantId { get; private set; }
    /// <summary>
    /// 证件类型
    /// </summary>
    public string AuthenticationType { get; private set; }
    /// <summary>
    /// 是否已认证通过
    /// </summary>
    public bool IsAuthenticated { get; private set; }


    public static Passport ParseFrom(ClaimsPrincipal user)
    {
        if (user == null || user.Identity == null)
            return null;

        var passport = new Passport();
        passport.IsAuthenticated = user.Identity.IsAuthenticated;
        passport.AuthenticationType = user.Identity.AuthenticationType;
        if (user.Identity.IsAuthenticated)
        {
            passport.Id = GetValue<int>(user, JwtRegisteredClaimNames.Sub);
            passport.UserAccount = user.FindFirst("userAccount")?.Value;
            passport.Name = user.FindFirst("userName")?.Value;
            passport.TenantType = GetValue<int>(user, "tenantType");
            passport.TenantId = GetValue<int>(user, "tenantId");
        }
        return passport;
    }
    private static T GetValue<T>(ClaimsPrincipal user, string type, T defaultValue = default)
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
