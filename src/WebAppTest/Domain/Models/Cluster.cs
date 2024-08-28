using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 集群表，描述消息队列的一个集群基本信息
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
    /// 绑定类型
    /// </summary>
    public string BindType { get; set; }
    /// <summary>
    /// 是否有状态
    /// </summary>
    public bool IsStateful { get; set; }
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
}
