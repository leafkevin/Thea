using System;

namespace Thea.Auth;

enum ResourceType
{
    /// <summary>
    /// 根菜单
    /// </summary>
    Menu,
    /// <summary>
    /// 二级菜单
    /// </summary>
    MenuItem,
    /// <summary>
    /// 页面
    /// </summary>
    Page,
    /// <summary>
    /// 功能按钮
    /// </summary>
    Operation
}
/// <summary>
/// 资源表，包含系统中的菜单、页面、操作按钮等
/// </summary>
class Resource
{
    /// <summary>
    /// 路由ID
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
    /// 跳转地址
    /// </summary>
    public string RedirectUrl { get; set; }
    /// <summary>
    /// Action地址
    /// </summary>
    public string ActionUrl { get; set; }
    /// <summary>
    /// 组件物理路径
    /// </summary>
    public string Component { get; set; }
    /// <summary>
    /// 路由名称
    /// </summary>
    public string RouterName { get; set; }
    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; }
    /// <summary>
    /// 是否固定
    /// </summary>
    public bool IsAffix { get; set; }
    /// <summary>
    /// 是否全屏显示
    /// </summary>
    public bool IsFull { get; set; }
    /// <summary>
    /// 是否隐藏
    /// </summary>
    public bool IsHidden { get; set; }
    /// <summary>
    /// 序号
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

/// <summary>
/// 角色资源关联表
/// </summary>
class Permission
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