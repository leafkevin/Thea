using System;

namespace WebAppTest.Domain.Models;

/// <summary>
/// 用户表，描述登录时所用的所有相关信息
/// </summary>
public class User
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public string UserId { get; set; }
    /// <summary>
    /// 用户名称
    /// </summary>
    public string UserName { get; set; }
    /// <summary>
    /// 登录账号
    /// </summary>
    public string Account { get; set; }
    /// <summary>
    /// 手机号码
    /// </summary>
    public string Mobile { get; set; }
    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; }
    /// <summary>
    /// 租户ID
    /// </summary>
    public string TenantId { get; set; }
    /// <summary>
    /// 密码
    /// </summary>
    public string Password { get; set; }
    /// <summary>
    /// 性别
    /// </summary>
    public Gender? Gender { get; set; }
    /// <summary>
    /// 生日
    /// </summary>
    public string BirthDate { get; set; }
    /// <summary>
    /// 盐
    /// </summary>
    public string Salt { get; set; }
    /// <summary>
    /// 头像
    /// </summary>
    public string AvatarUrl { get; set; }
    /// <summary>
    /// 解锁时间
    /// </summary>
    public DateTime LockedEnd { get; set; }
    /// <summary>
    /// 状态
    /// </summary>
    public int Status { get; set; }
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
