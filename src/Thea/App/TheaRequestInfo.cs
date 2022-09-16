using System;
namespace Thea;

public class TheaRequestInfo
{
    public string Id { get; set; }
    public string TraceId { get; set; }
    public int Sequence { get; set; } = 1;
    public ApiType ApiType { get; set; }
    public string Path { get; set; }
    public string ApiUrl { get; set; }
    public string ClientIp { get; set; }
    public string Host { get; set; }
    public string AppId { get; set; }
    public string Parameters { get; set; }
    public int ThreadId { get; set; }
    public string Tag { get; set; }


    public int UserId { get; set; }
    public string UserAccount { get; set; }
    public string UserName { get; set; }
    public int TenantType { get; set; }
    public int TenantId { get; set; }
    public string Authorization { get; set; }


    public DateTime CreatedAt { get; set; }
    public int? Elapsed { get; set; }
}
