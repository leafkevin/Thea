using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Web;
using Thea.Logging;

namespace Thea.Web;

public class TheaWebMiddleware
{
    private readonly RequestDelegate next;
    private readonly IConfiguration configuation;
    private readonly ILogger<TheaWebMiddleware> logger;

    public TheaWebMiddleware(RequestDelegate next, IConfiguration configuation, ILogger<TheaWebMiddleware> logger)
    {
        this.next = next;
        this.configuation = configuation;
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
            Exception excepition = null;
            try
            {
                context.Response.Body = memoryStream;
                await next(context);
            }
            catch (Exception ex)
            {
                excepition = ex;
            }
            var response = await this.ReadBody(memoryStream);
            response = this.ProcessCustomResponse(context, response, excepition, logEntityInfo);
            context.Response.Body = originalStream;
            await context.Response.WriteAsync(response);
            this.logger.LogEntity(logEntityInfo);
        }
    }
    private async Task<LogEntity> CreateLogEntity(HttpContext context)
    {
        var logEntityInfo = new LogEntity { Id = ObjectId.NewId(), LogLevel = LogLevel.Information };
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
        logEntityInfo.ClientIp = context.GetClientIp();
        var apiUrl = $"{context.Request.Scheme}://*{context.Request.PathBase.Value}{context.Request.Path.Value}";
        logEntityInfo.ApiUrl = HttpUtility.UrlDecode(apiUrl);
        logEntityInfo.CreatedAt = DateTime.Now;

        context.Request.EnableBuffering();
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("TraceId"))
                context.Response.Headers.Add("TraceId", logEntityInfo.TraceId);
            if (!context.Response.Headers.ContainsKey("Tag") && !string.IsNullOrEmpty(logEntityInfo.Tag))
                context.Response.Headers.Add("Tag", logEntityInfo.Tag);

            return Task.CompletedTask;
        });
        switch (logEntityInfo.ApiType)
        {
            case ApiType.HttpGet:
            case ApiType.HttpDelete:
                if (context.Request.Query != null && context.Request.Query.Count > 0)
                    logEntityInfo.Parameters = HttpUtility.UrlDecode(context.Request.QueryString.ToString());
                break;
            case ApiType.HttpPost:
            case ApiType.HttpPut:
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
    private string ProcessCustomResponse(HttpContext context, string originalResponse, Exception excepition, LogEntity logEntityInfo)
    {
        var response = originalResponse;
        logEntityInfo.StatusCode = context.Response.StatusCode;
        if (excepition != null && logEntityInfo.StatusCode == 200)
            logEntityInfo.StatusCode = 500;
        logEntityInfo.Body = $"Request finished. Status code: {logEntityInfo.StatusCode}";
        switch (context.Response.StatusCode)
        {
            case 401:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(logEntityInfo.StatusCode, "未授权，请登陆后重试！").ToJson();
                break;
            case 403:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(logEntityInfo.StatusCode, "没有权限访问该服务！").ToJson();
                break;
            case 404:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(logEntityInfo.StatusCode, "未找到服务！").ToJson();
                break;
            case 500:
            case 502:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(logEntityInfo.StatusCode, "服务器内部错误，Detail:" + excepition.ToString()).ToJson();
                logEntityInfo.Exception = excepition;
                logEntityInfo.Body = $"Request failed. An exception has happened. Status code: {logEntityInfo.StatusCode}";
                break;
        }
        if (excepition != null)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json;charset=utf-8";
            response = TheaResponse.Fail(logEntityInfo.StatusCode, "服务器内部错误，Detail:" + excepition.Message.ToString()).ToJson();
            logEntityInfo.Exception = excepition;
            logEntityInfo.Body = $"Request failed. An exception has happened. Status code: {logEntityInfo.StatusCode}";
        }
        if (context.Request.Headers.TryGetValue("Authorization", out StringValues authorization))
        {
            logEntityInfo.Authorization = authorization.ToString();
            if (context.User != null)
            {
                var passport = context.User.ToPassport();
                logEntityInfo.UserId = passport.UserId;
                logEntityInfo.UserName = passport.UserName;
                logEntityInfo.AppId = this.configuation["AppId"] ?? context.User.FindFirst("client_id")?.Value;
                logEntityInfo.TenantType = passport.TenantType;
                logEntityInfo.TenantId = passport.TenantId;
            }
        }
        logEntityInfo.Response = response;
        logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.CreatedAt).TotalMilliseconds;
        return response;
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
}