using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 授权表，描述一个角色所拥有的每个菜单项+功能按钮的关联，存在即授权
/// </summary>
public class Authorization
{
    /// <summary>
    /// 角色ID
    /// </summary>
    public string RoleId { get; set; }
    /// <summary>
    /// 菜单ID
    /// </summary>
    public string ResourceId { get; set; }
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
