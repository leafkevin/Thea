using System.Security.Principal;

namespace Thea;

public interface IPassport : IIdentity
{
    /// <summary>
    /// 用户ID
    /// </summary>
    int Id { get; }
    /// <summary>
    /// 用户账号
    /// </summary>
    string UserAccount { get; }
    /// <summary>
    /// 租户类型
    /// </summary>
    public int TenantType { get; }
    /// <summary>
    /// 租户ID
    /// </summary>
    public int TenantId { get; }
}
