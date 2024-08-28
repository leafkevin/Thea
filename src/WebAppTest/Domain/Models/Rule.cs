using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 规则表，描述所有可执行的规则
/// </summary>
public class Rule
{
    /// <summary>
    /// 规则ID
    /// </summary>
    public string RuleId { get; set; }
    /// <summary>
    /// 规则描述
    /// </summary>
    public string Description { get; set; }
    /// <summary>
    /// 规则表达式
    /// </summary>
    public string Expression { get; set; }
    /// <summary>
    /// 参数名列表
    /// </summary>
    public string Parameters { get; set; }
    /// <summary>
    /// 完成类型
    /// </summary>
    public int CompletionType { get; set; }
    /// <summary>
    /// 警告类型
    /// </summary>
    public int WarnType { get; set; }
    /// <summary>
    /// 警告内容
    /// </summary>
    public string WarnMessage { get; set; }
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
