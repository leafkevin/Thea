using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 用户角色表，描述用户与角色的关联关系
/// </summary>
public class UserRole
{
    /// <summary>
    /// 资源ID
    /// </summary>
    public string UserId { get; set; }
    /// <summary>
    /// 角色ID
    /// </summary>
    public string RoleId { get; set; }
    /// <summary>
    /// 最后更新人
    /// </summary>
    public string UpdatedBy { get; set; }
    /// <summary>
    /// 最后更新日期
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
