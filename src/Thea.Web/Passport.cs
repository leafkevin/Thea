using System;
using System.Globalization;
using System.Security.Claims;

namespace Thea.Web;

class Passport : IPassport
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public virtual string UserId { get; set; }
    /// <summary>
    /// 用户账号
    /// </summary>
    public virtual string UserAccount { get; set; }
    /// <summary>
    /// 用户姓名
    /// </summary>
    public string UserName { get; set; }
    /// <summary>
    /// 租户类型
    /// </summary>
    public virtual string TenantType { get; set; }
    /// <summary>
    /// 租户ID
    /// </summary>
    public virtual int TenantId { get; set; }
    /// <summary>
    /// 邮箱
    /// </summary>
    public virtual string Email { get; set; }

    public Passport(ClaimsPrincipal user)
    {
        if (user == null || user.Identity == null)
            return;
        if (user.Identity.IsAuthenticated)
        {
            var netId = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
            this.UserId = user.FindFirst("sub")?.Value ?? user.FindFirst(netId)?.Value;
            this.UserAccount = user.FindFirst("acc")?.Value;
            this.UserName = user.FindFirst("name")?.Value;
            this.TenantType = user.FindFirst("tenant_type")?.Value;
            this.TenantId = user.ClaimTo<int>("tenant", -1);
            var netEmail = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress";
            this.Email = user.FindFirst("email")?.Value ?? user.FindFirst(netEmail)?.Value;
        }
    }
}
