using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 资源表，包含菜单、权限按钮等资源
/// </summary>
public class Resource
{
    /// <summary>
    /// 资源ID
    /// </summary>
    public string ResourceId { get; set; }
    /// <summary>
    /// 资源名称
    /// </summary>
    public string ResourceName { get; set; }
    /// <summary>
    /// 资源类型
    /// </summary>
    public int ResourceType { get; set; }
    /// <summary>
    /// 父亲ID
    /// </summary>
    public string ParentId { get; set; }
    /// <summary>
    /// 是否外部连接
    /// </summary>
    public bool IsLink { get; set; }
    /// <summary>
    /// 路由地址
    /// </summary>
    public string RouteUrl { get; set; }
    /// <summary>
    /// 路由地址
    /// </summary>
    public string ActionUrl { get; set; }
    /// <summary>
    /// 组件物理路径
    /// </summary>
    public string Component { get; set; }
    /// <summary>
    /// 是否全屏显示
    /// </summary>
    public bool IsFull { get; set; }
    /// <summary>
    /// 是否固定标签页
    /// </summary>
    public bool IsAffix { get; set; }
    /// <summary>
    /// 排序
    /// </summary>
    public int Sequence { get; set; }
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
