using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 绑定信息表，描述消息队列每个消费者的绑定关系
/// </summary>
public class Binding
{
    /// <summary>
    /// 绑定ID
    /// </summary>
    public string BindingId { get; set; }
    /// <summary>
    /// 集群ID
    /// </summary>
    public string ClusterId { get; set; }
    /// <summary>
    /// 信箱
    /// </summary>
    public string Exchange { get; set; }
    /// <summary>
    /// 队列
    /// </summary>
    public string Queue { get; set; }
    /// <summary>
    /// 绑定类型
    /// </summary>
    public string BindType { get; set; }
    /// <summary>
    /// 绑定KEY
    /// </summary>
    public string BindingKey { get; set; }
    /// <summary>
    /// 主机名称
    /// </summary>
    public string HostName { get; set; }
    /// <summary>
    /// 预取个数
    /// </summary>
    public int PrefetchCount { get; set; }
    /// <summary>
    /// 是否单一激活消费者
    /// </summary>
    public bool IsSingleActiveConsumer { get; set; }
    /// <summary>
    /// 是否应答队列
    /// </summary>
    public bool IsReply { get; set; }
    /// <summary>
    /// 是否延时消费者
    /// </summary>
    public bool IsDelay { get; set; }
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
