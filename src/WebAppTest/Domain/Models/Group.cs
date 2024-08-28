using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 组表，描述拥有指定数据权限的用户归属的数据权限组
/// </summary>
public class Group
{
    /// <summary>
    /// 组ID
    /// </summary>
    public string GroupId { get; set; }
    /// <summary>
    /// 组名称
    /// </summary>
    public string GroupName { get; set; }
    /// <summary>
    /// 父亲ID
    /// </summary>
    public string ParentId { get; set; }
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }
    /// <summary>
    /// 创建人
    /// </summary>
    public string CreatedBy { get; set; }
    /// <summary>
    /// 创建日期
    /// </summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// 最后更新人
    /// </summary>
    public string UpdatedBy { get; set; }
    /// <summary>
    /// 最后更新日期
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
