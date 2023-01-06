public interface IPassport
{
    /// <summary>
    /// 用户ID
    /// </summary>
    int UserId { get; }
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
    public int TenantType { get; }
    /// <summary>
    /// 租户ID
    /// </summary>
    public int TenantId { get; }
}