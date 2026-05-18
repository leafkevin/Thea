using System;
using System.Collections.Generic;

namespace Thea.MessageDriven;

/// <summary>
/// 配置表，描述所有的队列、交换机绑定等信息
/// </summary>
public class Setting
{
    /// <summary>
    /// 队列名称
    /// </summary>
    public string Queue { get; set; }
    /// <summary>
    /// 交换机
    /// </summary>
    public List<string> Exchanges { get; set; }
    /// <summary>
    /// 绑定类型
    /// </summary>
    public string BindType { get; set; }
    /// <summary>
    /// 绑定KEY
    /// </summary>
    public string BindingKey { get; set; }
    /// <summary>
    /// 是否仲裁队列
    /// </summary>
    public bool IsQuorumQueue { get; set; }
    /// <summary>
    /// 是否有状态
    /// </summary>
    public bool IsStateful { get; set; }
    /// <summary>
    /// 是否单一激活消费者
    /// </summary>
    public bool IsSingleActiveConsumer { get; set; }
    /// <summary>
    /// 是否需要转发
    /// </summary>
    public bool IsNeedTransfer { get; set; }
    /// <summary>
    /// 是否延迟消息
    /// </summary>
    public bool IsDelay { get; set; }
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

    public Setting Clone() => this.MemberwiseClone() as Setting;
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