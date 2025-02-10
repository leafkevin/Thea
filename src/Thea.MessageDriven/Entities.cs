using System;

namespace Thea.MessageDriven;

/// <summary>
/// 集群表，描述消息驱动所有的业务集群，一个业务一个集群
/// </summary>
public class Cluster
{
    /// <summary>
    /// 集群ID
    /// </summary>
    public string ClusterId { get; set; }
    /// <summary>
    /// 集群名称
    /// </summary>
    public string ClusterName { get; set; }
    /// <summary>
    /// 信箱
    /// </summary>
    public string Exchange { get; set; }
    /// <summary>
    /// 绑定类型
    /// </summary>
    public string BindType { get; set; }
    /// <summary>
    /// 绑定Key
    /// </summary>
    public string BindingKey { get; set; }
    /// <summary>
    /// 队列名字或是队列前缀
    /// </summary>
    public string Queue { get; set; }
    /// <summary>
    /// 工作负荷个数，有状态时是队列个数，无状态时是队列的消费者个数，0表示无限制，几个负载几个消费者
    /// </summary>
    public int WorkloadTotal { get; set; }
    /// <summary>
    /// 是否有状态
    /// </summary>
    public bool IsStateful { get; set; }
    /// <summary>
    /// 是否单一激活消费者
    /// </summary>
    public bool IsSac { get; set; }
    /// <summary>
    /// 是否延迟消息
    /// </summary>
    public bool IsDelay { get; set; }
    /// <summary>
    /// 预取消息个数
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
    /// 最后更新日期
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
/// <summary>
/// 消息驱动日志表
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
    /// 路由
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
    /// 返回值
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