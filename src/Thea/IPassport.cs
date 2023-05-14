public interface IPassport
{
    /// <summary>
    /// 用户ID
    /// </summary>
    string UserId { get; }
    /// <summary>
    /// 用户姓名
    /// </summary>
    string UserName { get; }
    /// <summary>
    /// 用户账号
    /// </summary>
    string UserAccount { get; }
    /// <summary>
    /// 租户类型
    /// </summary>
    string TenantType { get; }
    /// <summary>
    /// 租户ID
    /// </summary>
    int? TenantId { get; }
    /// <summary>
    /// 邮箱地址
    /// </summary>
    string Email { get; }
    /// <summary>
    /// 角色
    /// </summary>
    string RoleId { get; }
}