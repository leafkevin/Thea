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
    /// 应用ID
    /// </summary>
    public virtual string AppId { get; set; }

    public Passport(ClaimsPrincipal user)
    {
        if (user == null || user.Identity == null)
            return;
        if (user.Identity.IsAuthenticated)
        {
            this.UserId = user.FindFirst("sub").Value;
            this.UserAccount = user.FindFirst("user_name")?.Value;
            this.UserName = user.FindFirst("name")?.Value;
            this.AppId = user.FindFirst("client_id")?.Value;
            this.TenantType = user.FindFirst("tenant_type")?.Value;
            this.TenantId = user.ClaimTo<int>("tenant_id", -1);          
        }
    }
}
