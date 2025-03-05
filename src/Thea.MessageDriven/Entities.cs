using System;

namespace Thea.MessageDriven;

/// <summary>
/// 绑定表，描述交换机与队列的绑定关系
/// </summary>
public class Binding
{
    /// <summary>
    /// 信箱ID
    /// </summary>
    public string ExchangeId { get; set; }
    /// <summary>
    /// 队列ID
    /// </summary>
    public string QueueId { get; set; }
    /// <summary>
    /// 绑定类型
    /// </summary>
    public string BindType { get; set; }
    /// <summary>
    /// 绑定KEY
    /// </summary>
    public string BindingKey { get; set; }
    /// <summary>
    /// 是否需要转发
    /// </summary>
    public bool IsNeedTransfer { get; set; }
    /// <summary>
    /// 是否延时消费者
    /// </summary>
    public bool IsDelay { get; set; }
}
/// <summary>
/// 队列表，描述所有的队列基本信息
/// </summary>
public class Queue
{
    /// <summary>
    /// 队列ID
    /// </summary>
    public string QueueId { get; set; }
    /// <summary>
    /// 队列名称
    /// </summary>
    public string QueueName { get; set; }
    /// <summary>
    /// 是否有状态
    /// </summary>
    public bool IsStateful { get; set; }
    /// <summary>
    /// 工作负荷个数
    /// </summary>
    public int WorkloadTotal { get; set; }
    /// <summary>
    /// 是否单一激活消费者
    /// </summary>
    public bool IsSac { get; set; }
    /// <summary>
    /// 预取个数
    /// </summary>
    public int PrefetchCount { get; set; }
    /// <summary>
    /// 是否开启日志
    /// </summary>
    public bool IsLogEnabled { get; set; }
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }
}
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
    /// 信箱ID
    /// </summary>
    public string ExchangeId { get; set; }
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
    /// 最后更新日期
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}