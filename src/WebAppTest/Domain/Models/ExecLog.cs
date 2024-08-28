using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 日志表，描述消息队列每个消费者的执行日志
/// </summary>
public class ExecLog
{
    /// <summary>
    /// 日志ID
    /// </summary>
    public string LogId { get; set; }
    /// <summary>
    /// 集群ID
    /// </summary>
    public string ClusterId { get; set; }
    /// <summary>
    /// 路由KEY
    /// </summary>
    public string RoutingKey { get; set; }
    /// <summary>
    /// 队列
    /// </summary>
    public string Queue { get; set; }
    /// <summary>
    /// 消息内容
    /// </summary>
    public string Body { get; set; }
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }
    /// <summary>
    /// 执行结果
    /// </summary>
    public string Result { get; set; }
    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryTimes { get; set; }
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
