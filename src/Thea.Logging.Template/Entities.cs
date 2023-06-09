using System;

namespace Thea.Logging.Template;

class OperationLog
{
    public string Id { get; set; }
    public int TenantId { get; set; }
    public string Category { get; set; }
    public string UserId { get; set; }
    public string ApiUrl { get; set; }
    public string Tag { get; set; }
    public string Body { get; set; }
    public string ClientIp { get; set; }
    public DateTime CreatedAt { get; set; }
}
class LogTemplate
{
    public string Id { get; set; }
    public int TenantId { get; set; }
    public string Category { get; set; }
    public string ApiUrl { get; set; }
    public string TagFrom { get; set; }
    public string TagRegex { get; set; }
    public string Template { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime ReviseTime { get; set; }
}