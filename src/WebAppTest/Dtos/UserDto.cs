using WebAppTest.Domain;

namespace WebAppTest.Dtos;

public class UserDto
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
    /// 性别
    /// </summary>
    public Gender? Gender { get; set; }
    /// <summary>
    /// 生日
    /// </summary>
    public string BirthDate { get; set; }
    /// <summary>
    /// 头像
    /// </summary>
    public string AvatarUrl { get; set; }
}
