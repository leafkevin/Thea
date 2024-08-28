using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 数据表，描述数据权限的权限数据
/// </summary>
public class Data
{
    /// <summary>
    /// 数据类型
    /// </summary>
    public string DataType { get; set; }
    /// <summary>
    /// 数据ID
    /// </summary>
    public string DataId { get; set; }
    /// <summary>
    /// 数据值
    /// </summary>
    public string DataValue { get; set; }
    /// <summary>
    /// 组ID
    /// </summary>
    public string GroupId { get; set; }
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
