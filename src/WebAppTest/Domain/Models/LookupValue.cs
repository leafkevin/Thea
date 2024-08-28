using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 参数值表，描述系统中所有的类型，种类等有限集合字典数据
/// </summary>
public class LookupValue
{
    /// <summary>
    /// 参数ID
    /// </summary>
    public string LookupId { get; set; }
    /// <summary>
    /// 参数值
    /// </summary>
    public string Value { get; set; }
    /// <summary>
    /// 参数文本
    /// </summary>
    public string LookupText { get; set; }
    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; }
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
