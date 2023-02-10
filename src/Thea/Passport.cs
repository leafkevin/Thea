using System;
using System.Globalization;
using System.Security.Claims;

namespace Thea;

public class Passport : IPassport
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; private set; }
    /// <summary>
    /// 用户账号
    /// </summary>
    public string UserAccount { get; private set; }
    /// <summary>
    /// 用户姓名
    /// </summary>
    public string UserName { get; private set; }
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
            passport.UserId = GetValue<int>(user, JwtClaimTypes.Subject);
            passport.UserAccount = user.FindFirst("userAccount")?.Value;
            passport.UserName = user.FindFirst("userName")?.Value;
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

    class JwtClaimTypes
    {
        public const string Id = "id";
        public const string JwtId = "jti";
        public const string Issuer = "iss";
        public const string IssuedAt = "iat";
        public const string SessionId = "sid";
        public const string Audience = "aud";
        public const string Subject = "sub";
        public const string StateHash = "s_hash";
        public const string Actor = "act";
        public const string Nonce = "nonce";
        public const string Scope = "scope";
        public const string Role = "role";
        public const string WebSite = "website";
        public const string ClientId = "client_id";
        public const string IdentityProvider = "idp";
        public const string AccessTokenHash = "at_hash";
        public const string Expiration = "exp";
        public const string NotBefore = "nbf";
        public const string AuthenticationMethod = "amr";
        public const string Confirmation = "cnf";
        public const string AuthorizationCodeHash = "c_hash";
        public const string AuthorizedParty = "azp";
        public const string AuthenticationTime = "auth_time";
        public const string NickName = "nickname";
        public const string Picture = "picture";
        public const string Gender = "gender";
        public const string BirthDate = "birthdate";
        public const string Profile = "profile";
        public const string Address = "address";
        public const string Email = "email";
        public const string Phone = "phone";
        public const string Events = "events";
        public const string ReferenceTokenId = "reference_token_id";
    }
}
