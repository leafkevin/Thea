using System;

namespace Thea.Logging;

public class LogEntity
{
    public string Id { get; set; }
    public string TraceId { get; set; }
    public string AppId { get; set; }
    public string Environment { get; set; }
    public int LogLevel { get; set; }
    public int ApiType { get; set; }
    public string ApiUrl { get; set; }
    public string ClientIp { get; set; }
    public string Host { get; set; }
    public string Headers { get; set; }
    public string Request { get; set; }

    public string UserId { get; set; }
    public string UserName { get; set; }
    public string TenantId { get; set; }
    public string Authorization { get; set; }

    public string Tag { get; set; }
    public string Body { get; set; }
    public int StatusCode { get; set; }
    public string Response { get; set; }

    [JsonIgnore]
    public object Exception { get; set; }
    public DateTime LogTime { get; set; } = DateTime.Now;
    public int? Elapsed { get; set; }
    /// <summary>
    /// 为false时，可抛弃日志记录
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public override string ToString() => this.Body;

    public LogEntity DecorateFrom(LogEntity lastScopeState)
    {
        if (string.IsNullOrEmpty(this.TraceId) && !string.IsNullOrEmpty(lastScopeState.TraceId))
            this.TraceId = lastScopeState.TraceId;
        if (string.IsNullOrEmpty(this.Tag) && !string.IsNullOrEmpty(lastScopeState.Tag))
            this.Tag = lastScopeState.Tag;

        if (string.IsNullOrEmpty(this.TenantId) && !string.IsNullOrEmpty(lastScopeState.TenantId))
            this.TenantId = lastScopeState.TenantId;
        if (string.IsNullOrEmpty(this.UserId) && !string.IsNullOrEmpty(lastScopeState.UserId))
            this.UserId = lastScopeState.UserId;
        if (string.IsNullOrEmpty(this.UserName) && !string.IsNullOrEmpty(lastScopeState.UserName))
            this.UserName = lastScopeState.UserName;
        if (string.IsNullOrEmpty(this.Authorization) && !string.IsNullOrEmpty(lastScopeState.Authorization))
            this.Authorization = lastScopeState.Authorization;

        if (string.IsNullOrEmpty(this.ApiUrl) && !string.IsNullOrEmpty(lastScopeState.ApiUrl))
            this.ApiUrl = lastScopeState.ApiUrl;
        if (string.IsNullOrEmpty(this.Headers) && !string.IsNullOrEmpty(lastScopeState.Headers))
            this.Headers = lastScopeState.Headers;
        if (string.IsNullOrEmpty(this.Request) && !string.IsNullOrEmpty(lastScopeState.Request))
            this.Request = lastScopeState.Request;
        if (string.IsNullOrEmpty(this.Response) && !string.IsNullOrEmpty(lastScopeState.Response))
            this.Response = lastScopeState.Response;
        if (string.IsNullOrEmpty(this.Body) && !string.IsNullOrEmpty(lastScopeState.Body))
            this.Body = lastScopeState.Body;

        if (string.IsNullOrEmpty(this.Host) && !string.IsNullOrEmpty(lastScopeState.Host))
            this.Host = lastScopeState.Host;
        if (string.IsNullOrEmpty(this.ClientIp) && !string.IsNullOrEmpty(lastScopeState.ClientIp))
            this.ClientIp = lastScopeState.ClientIp;
        return this;
    }
}
