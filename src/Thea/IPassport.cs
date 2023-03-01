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
    public int TenantType { get; }
	/// <summary>
    /// 认证类型
    /// </summary>
    string AuthenticationType { get; }
    /// <summary>
    /// 是否已认证通过
    /// </summary>
    bool IsAuthenticated { get; }
}