using System;

namespace Thea.Logging;

public class LogEntity
{
    public string Id { get; set; }
    public string TraceId { get; set; }
    public int Sequence { get; set; } = 1;
    public int LogLevel { get; set; }
    public int ApiType { get; set; }
    public string ApiUrl { get; set; }
    public string ClientIp { get; set; }
    public string Host { get; set; }
    public string Source { get; set; }
    public string AppId { get; set; }
    public string Parameters { get; set; }

    public string UserId { get; set; }
    public string UserName { get; set; }
    public string TenantId { get; set; }
    public string Authorization { get; set; }

    public string Tag { get; set; }
    public string Body { get; set; }
    public int StatusCode { get; set; }
    public string Response { get; set; }

    public object Exception { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LogTime { get; set; }
    public int? Elapsed { get; set; }

    public override string ToString() => this.Body;
}
