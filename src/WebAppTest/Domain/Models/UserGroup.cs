using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 用户组表，描述每个用户归属哪个数据权限组
/// </summary>
public class UserGroup
{
    /// <summary>
    /// 组ID
    /// </summary>
    public string GroupId { get; set; }
    /// <summary>
    /// 组名称
    /// </summary>
    public string UserId { get; set; }
    /// <summary>
    /// 最后更新人
    /// </summary>
    public string UpdatedBy { get; set; }
    /// <summary>
    /// 最后更新日期
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
