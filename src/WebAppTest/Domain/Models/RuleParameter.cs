using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 规则参数表，描述所有规则参数的定义 
/// </summary>
public class RuleParameter
{
    /// <summary>
    /// 参数ID
    /// </summary>
    public string ParameterId { get; set; }
    /// <summary>
    /// 类型名称
    /// </summary>
    public string TypeName { get; set; }
    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; }
    /// <summary>
    /// 获取服务名称
    /// </summary>
    public string ServiceName { get; set; }
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
