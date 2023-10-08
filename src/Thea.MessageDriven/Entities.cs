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
    /// 绑定类型
    /// </summary>
    public string BindType { get; set; }
    /// <summary>
    /// 连接URL
    /// </summary>
    public string Url { get; set; }
    /// <summary>
    /// 用户名
    /// </summary>
    public string User { get; set; }
    /// <summary>
    /// 密码
    /// </summary>
    public string Password { get; set; }
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
/// <summary>
/// 队列绑定表，描述消息驱动所有的业务队列与信箱的绑定关系
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
    /// 绑定Key
    /// </summary>
    public string BindingKey { get; set; }
    /// <summary>
    /// 主机名称
    /// </summary>
    public string HostName { get; set; }
    /// <summary>
    /// 预取消息个数
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
/// <summary>
/// 消费者表，描述消息驱动所有的业务的消费者
/// </summary>
public class Consumer
{
    /// <summary>
    /// 消费者ID
    /// </summary>
    public string ConsumerId { get; set; }
    /// <summary>
    /// 集群ID
    /// </summary>
    public string ClusterId { get; set; }
    /// <summary>
    /// 队列
    /// </summary>
    public string Queue { get; set; }
    /// <summary>
    /// 是否应答消费者
    /// </summary>
    public bool IsReply { get; set; }
    /// <summary>
    /// 主机名称
    /// </summary>
    public string HostName { get; set; }
    /// <summary>
    /// IP地址
    /// </summary>
    public string IpAddress { get; set; }
    /// <summary>
    /// 最后运行时间
    /// </summary>
    public DateTime LastExecutedTime { get; set; }
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