using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Thea.Logging;

namespace Thea.Web;

public class TheaWebMiddleware
{
    private readonly RequestDelegate next;
    private readonly IConfiguration configuation;
    private readonly IResponseFilter responseFilter;
    private readonly ILogger<TheaWebMiddleware> logger;

    public TheaWebMiddleware(RequestDelegate next, IConfiguration configuation, IResponseFilter responseFilter, ILogger<TheaWebMiddleware> logger)
    {
        this.next = next;
        this.configuation = configuation;
        this.responseFilter = responseFilter;
        this.logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalStream = context.Response.Body;
        var logEntityInfo = await this.CreateLogEntity(context);
        var logScope = new TheaLogState { TraceId = logEntityInfo.TraceId, Sequence = logEntityInfo.Sequence, Tag = logEntityInfo.Tag };
        using (this.logger.BeginScope(logScope))
        {
            using var memoryStream = new MemoryStream();
            Exception exception = null;
            try
            {
                context.Response.Body = memoryStream;
                await next(context);
            }
            catch (Exception ex)
            {
                exception = ex.InnerException ?? ex;
                logEntityInfo.Exception = exception;
            }
            var response = await this.responseFilter.ProcessRequest(context, memoryStream, exception);
            context.Response.Body = originalStream;
            if (exception != null)
            {
                logEntityInfo.StatusCode = context.Response.StatusCode;
                logEntityInfo.Body = $"Request failed. An exception has happened. Status code: {logEntityInfo.StatusCode}";
                logEntityInfo.Response = TheaResponse.Fail(logEntityInfo.StatusCode, exception.ToString()).ToJson();
            }
            logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.CreatedAt).TotalMilliseconds;
            if (context.Request.Headers.TryGetValue("Authorization", out var authorization))
            {
                logEntityInfo.Authorization = authorization.ToString();
                if (context.User != null)
                {
                    var passport = context.User.ToPassport();
                    logEntityInfo.UserId = passport.UserId;
                    logEntityInfo.UserName = passport.UserName;
                    logEntityInfo.AppId = this.configuation["AppId"];
                    logEntityInfo.TenantId = passport.TenantId;
                }
            }
            await context.Response.WriteAsync(response);
            this.logger.LogEntity(logEntityInfo);
        }
    }
    private async Task<LogEntity> CreateLogEntity(HttpContext context)
    {
        var logEntityInfo = new LogEntity { Id = ObjectId.NewId(), LogLevel = (int)LogLevel.Information };
        if (context.Request.Headers.TryGetValue("TraceId", out var traceIds))
        {
            var traceId = traceIds.ToString();
            context.TraceIdentifier = traceId;
            logEntityInfo.TraceId = traceId;
            if (context.Request.Headers.TryGetValue("Sequence", out var sequence))
                logEntityInfo.Sequence = int.Parse(sequence.ToString());
        }
        else
        {
            context.TraceIdentifier = context.TraceIdentifier.Replace(":", "-");
            logEntityInfo.TraceId = context.TraceIdentifier;
            context.Request.Headers.Append("TraceId", new StringValues(logEntityInfo.TraceId));
            context.Request.Headers.Append("Sequence", new StringValues(logEntityInfo.Sequence.ToString()));
        }
        if (context.Request.Headers.TryGetValue("Tag", out var tag))
            logEntityInfo.Tag = tag.ToString();

        logEntityInfo.Host = GetHost();
        logEntityInfo.ApiType = this.GetApiType(context.Request.Method);
        logEntityInfo.ClientIp = context.GetClientIp();
        var apiUrl = $"{context.Request.Scheme}://*{context.Request.PathBase.Value}{context.Request.Path.Value}";
        logEntityInfo.ApiUrl = HttpUtility.UrlDecode(apiUrl);
        logEntityInfo.CreatedAt = DateTime.Now;

        context.Request.EnableBuffering();
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("TraceId"))
                context.Response.Headers.Append("TraceId", logEntityInfo.TraceId);
            if (!context.Response.Headers.ContainsKey("Tag") && !string.IsNullOrEmpty(logEntityInfo.Tag))
                context.Response.Headers.Append("Tag", logEntityInfo.Tag);

            return Task.CompletedTask;
        });
        switch (logEntityInfo.ApiType)
        {
            case (int)ApiType.HttpGet:
            case (int)ApiType.HttpDelete:
                if (context.Request.Query != null && context.Request.Query.Count > 0)
                    logEntityInfo.Parameters = HttpUtility.UrlDecode(context.Request.QueryString.ToString());
                break;
            case (int)ApiType.HttpPost:
            case (int)ApiType.HttpPut:
                if (context.Request.Query != null && context.Request.Query.Count > 0)
                    logEntityInfo.Parameters = "QueryString: {HttpUtility.UrlDecode(context.Request.QueryString.ToString())} \nBody: ";
                logEntityInfo.Parameters += await this.ReadBody(context.Request.Body);
                break;
        }

        return logEntityInfo;
    }
    private async Task<string> ReadBody(Stream stream)
    {
        stream.Position = 0;
        var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();
        stream.Position = 0;
        return result;
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
    private int GetApiType(string httpMethod)
    {
        switch (httpMethod.ToUpper())
        {
            case "GET": return (int)ApiType.HttpGet;
            case "POST": return (int)ApiType.HttpPost;
            case "PUT": return (int)ApiType.HttpPut;
            case "DELETE": return (int)ApiType.HttpDelete;
        }
        return (int)ApiType.LocalInvoke;
    }
}