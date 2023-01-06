using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using Thea.Logging;

namespace Thea.Web;

public class TheaWebMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<TheaWebMiddleware> logger;

    public TheaWebMiddleware(RequestDelegate next, ILogger<TheaWebMiddleware> logger)
    {
        this.next = next;
        this.logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalStream = context.Response.Body;
        var logEntityInfo = await this.CreateLogEntity(context);
        this.Initialize(context, logEntityInfo);
        var logScope = new TheaLogState { TraceId = logEntityInfo.TraceId, Sequence = logEntityInfo.Sequence, Tag = logEntityInfo.Tag };
        using (this.logger.BeginScope(logScope))
        {
            using var memoryStream = new MemoryStream();
            try
            {
                context.Response.Body = memoryStream;
                await next(context);
                memoryStream.Position = 0;
                var respBody = new StreamReader(memoryStream).ReadToEnd();
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalStream);
                this.UpdateLogEntityInfo(context, respBody, logEntityInfo);
                this.logger.LogEntity(logEntityInfo);
            }
            catch (Exception ex)
            {
                await this.ProcessException(context, ex, logEntityInfo);
                memoryStream.Position = 0;
                var respBody = new StreamReader(memoryStream).ReadToEnd();
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalStream);

                this.logger.LogEntity(logEntityInfo);
            }
        }
    }

    private async Task<LogEntity> CreateLogEntity(HttpContext context)
    {
        var logEntityInfo = new LogEntity { Id = ObjectId.NewId(), LogLevel = LogLevel.Information };
        if (context.Request.Headers.TryGetValue("Authorization", out StringValues authorization))
            logEntityInfo.Authorization = authorization.ToString();

        if (context.User != null)
        {
            logEntityInfo.UserId = this.GetValue<int>(context.User, "sub");
            logEntityInfo.UserName = context.User.FindFirst("customerCode")?.Value;
            logEntityInfo.AppId = context.User.FindFirst("client_id")?.Value;
            logEntityInfo.TenantType = this.GetValue<int>(context.User, "customerId");
            logEntityInfo.TenantId = this.GetValue<int>(context.User, "customerId");
        }

        if (context.Request.Headers.TryGetValue("TraceId", out StringValues traceIds))
        {
            var traceId = traceIds.ToString();
            context.TraceIdentifier = traceId;
            logEntityInfo.TraceId = traceId;
            if (context.Request.Headers.TryGetValue("Sequence", out StringValues sequence))
                logEntityInfo.Sequence = int.Parse(sequence.ToString());
        }
        else
        {
            context.TraceIdentifier = context.TraceIdentifier.Replace(":", "-");
            logEntityInfo.TraceId = context.TraceIdentifier;
            context.Request.Headers.Add("TraceId", new StringValues(logEntityInfo.TraceId));
            context.Request.Headers.Add("Sequence", new StringValues(logEntityInfo.Sequence.ToString()));
        }
        if (context.Request.Headers.TryGetValue("Tag", out StringValues tag))
            logEntityInfo.Tag = tag.ToString();

        logEntityInfo.Host = GetHost();
        logEntityInfo.ApiType = this.GetApiType(context.Request.Method);
        logEntityInfo.ClientIp = GetClientIpAddress(context.Request);
        var path = $"{context.Request.PathBase.Value}{context.Request.Path.Value}";
        var apiUrl = $"{context.Request.Scheme}://*{path}{context.Request.QueryString.Value}";
        logEntityInfo.Path = $"{context.Request.PathBase.Value}{context.Request.Path.Value}";
        logEntityInfo.ApiUrl = HttpUtility.UrlDecode(apiUrl);
        logEntityInfo.CreatedAt = DateTime.Now;

        string queryString = null;
        if (!string.IsNullOrEmpty(context.Request.QueryString.Value))
            queryString = HttpUtility.UrlDecode(context.Request.QueryString.Value);

        if (logEntityInfo.ApiType == ApiType.HttpGet)
            logEntityInfo.Parameters = queryString;
        else
        {
            var body = await this.ReadBody(context.Request.Body);
            logEntityInfo.Parameters = $"{{QurerString:{queryString},Body:{body}}}";
        }

        return logEntityInfo;
    }

    private void Initialize(HttpContext context, LogEntity logEntityInfo)
    {
        context.Request.EnableBuffering();
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("TraceId"))
                context.Response.Headers.Add("TraceId", logEntityInfo.TraceId);
            if (!context.Response.Headers.ContainsKey("Tag") && !string.IsNullOrEmpty(logEntityInfo.Tag))
                context.Response.Headers.Add("Tag", logEntityInfo.Tag);

            return Task.CompletedTask;
        });
    }
    private async Task<string> ReadBody(Stream stream)
    {
        var originalPosition = stream.Position;
        stream.Position = 0;
        var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();
        stream.Position = originalPosition;
        return result;
    }
    private void UpdateLogEntityInfo(HttpContext context, string respBody, LogEntity logEntityInfo)
    {
        logEntityInfo.Response = respBody;
        logEntityInfo.StatusCode = context.Response.StatusCode;
        logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.CreatedAt).TotalMilliseconds;
        logEntityInfo.Body = $"Request finished. Status code: {logEntityInfo.StatusCode}";
    }

    private async Task ProcessException(HttpContext context, Exception ex, LogEntity logEntityInfo)
    {
        logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.CreatedAt).TotalMilliseconds;
        logEntityInfo.StatusCode = 500;
        logEntityInfo.Body =
            $"Request error. Status code:{logEntityInfo.StatusCode}, Error message: {Environment.NewLine}{ex.Message}";
        logEntityInfo.Exception = ex;
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json;charset=utf-8";
        var result = TheaResponse.Fail(-1, "服务器内部错误，Detail:" + ex.ToString());
        await context.Response.WriteAsync(result.ToJson());
    }

    private string GetClientIpAddress(HttpRequest request)
    {
        string result = null;
        if (TryGetHeaderValue(request, "X-Forwarded-For", "unknown", out result)) return result;
        else if (TryGetHeaderValue(request, "X-Real-IP", "unknown", out result)) return result;
        else if (TryGetHeaderValue(request, "X-Original-For", "unknown", out result)) return result;
        else if (TryGetHeaderValue(request, "Proxy-Client-IP", "unknown", out result)) return result;
        else if (TryGetHeaderValue(request, "WL-Proxy-Client-IP", "unknown", out result)) return result;
        else if (TryGetHeaderValue(request, "HTTP_CLIENT_IP", "unknown", out result)) return result;
        else if (TryGetHeaderValue(request, "HTTP_X_FORWARDED_FOR", "unknown", out result)) return result;
        else return request.HttpContext.Connection.RemoteIpAddress?.ToString() + ":" + request.HttpContext.Connection.RemotePort;
    }

    private static string GetHost()
    {
        foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (item.NetworkInterfaceType == NetworkInterfaceType.Ethernet && item.OperationalStatus == OperationalStatus.Up)
            {
                var properties = item.GetIPProperties();
                if (properties.GatewayAddresses.Count > 0)
                {
                    foreach (var ip in properties.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return ip.Address.ToString();
                        }
                    }
                }
            }
        }
        return string.Empty;
    }

    private bool TryGetHeaderValue(HttpRequest request, string key, string ignoreValue, out string result)
    {
        if (!request.Headers.ContainsKey(key))
        {
            result = null;
            return false;
        }

        if (String.Compare(request.Headers[key], ignoreValue, true) == 0)
        {
            result = null;
            return false;
        }

        result = request.Headers[key];
        return true;
    }

    private ApiType GetApiType(string httpMethod)
    {
        switch (httpMethod.ToUpper())
        {
            case "GET": return ApiType.HttpGet;
            case "POST": return ApiType.HttpPost;
            case "PUT": return ApiType.HttpPut;
            case "DELETE": return ApiType.HttpDelete;
        }
        return ApiType.LocalInvoke;
    }
    private T GetValue<T>(ClaimsPrincipal claimsPrincipal, string type)
    {
        var strValue = claimsPrincipal.FindFirst(type)?.Value;
        if (string.IsNullOrEmpty(strValue)) return default(T);
        return (T)Convert.ChangeType(strValue, typeof(T));
    }
}