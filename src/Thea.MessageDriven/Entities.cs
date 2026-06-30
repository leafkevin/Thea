using System;
using System.Collections.Generic;

namespace Thea.MessageDriven;

/// <summary>
/// 绑定表，描述交换机与队列的绑定关系
/// </summary>
public class Binding
{
    /// <summary>
    /// 交换机ID
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
    /// 应用ID
    /// </summary>
    public string AppId { get; set; }
    /// <summary>
    /// 是否有状态
    /// </summary>
    public bool IsStateful { get; set; }
    /// <summary>
    /// 工作负荷个数
    /// </summary>
    public int WorkloadTotal { get; set; }
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


    /// <summary>
    /// 是否仲裁队列
    /// </summary>
    public bool IsQuorumQueue { get; set; }
    /// <summary>
    /// 是否单一激活消费者
    /// </summary>
    public bool IsSingleActiveConsumer { get; set; }
}
class ConfigInfo
{
    public List<string> EndPoints { get; set; }
    public string User { get; set; }
    public string Password { get; set; }
    public int SacCount { get; set; } = 2;
    public int Heartbeat { get; set; } = 10;
    public int RpcTimeout { get; set; } = 30;
    public string DbKey { get; set; }
    public bool IsAllowCreateQueue { get; set; } = true;
    public bool IsAllowCreateExchange { get; set; } = true;
    public bool IsAllowCreateBinding { get; set; } = true;
}
class QueueState
{
    /// <summary>
    /// 是否仲裁队列
    /// </summary>
    public bool IsQuorumQueue { get; set; }
    /// <summary>
    /// 是否单一激活消费者
    /// </summary>
    public bool IsSingleActiveConsumer { get; set; }
}
class ExchangeTransfer
{
    public string FromExchange { get; set; }
    public string ToExchange { get; set; }
    public string RoutingKey { get; set; }
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
    /// 跟踪ID
    /// </summary>
    public string TraceId { get; set; }
    /// <summary>
    /// 交换机
    /// </summary>
    public string Exchange { get; set; }
    /// <summary>
    /// 队列名称
    /// </summary>
    public string Queue { get; set; }
    /// <summary>
    /// 路由KEY
    /// </summary>
    public string RoutingKey { get; set; }
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
    /// 最后更新人
    /// </summary>
    public string UpdatedBy { get; set; }
    /// <summary>
    /// 最后更新日期
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}