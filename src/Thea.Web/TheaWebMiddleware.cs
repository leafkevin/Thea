using System;
using System.Collections.Generic;
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
    private readonly string appId;
    private readonly RequestDelegate next;
    private readonly IResponseDecorator responseDecorator;
    private readonly ILogger<TheaWebMiddleware> logger;

    public TheaWebMiddleware(RequestDelegate next, IConfiguration configuation, IResponseDecorator responseDecorator, ILogger<TheaWebMiddleware> logger)
    {
        this.appId = configuation.GetValue<string>("AppId");
        if (string.IsNullOrEmpty(this.appId))
            throw new ArgumentNullException("AppId is required in configuration.");

        this.next = next;
        this.responseDecorator = responseDecorator;
        this.logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var originalStream = context.Response.Body;
        var logEntityInfo = await this.CreateLogEntity(context);
        var logScope = new TheaLogState { TraceId = logEntityInfo.TraceId, Tag = logEntityInfo.Tag };
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
                logEntityInfo.LogLevel = (int)LogLevel.Error;
            }
            logEntityInfo.Response = await this.responseDecorator.ProcessRequest(context, memoryStream, exception);
            context.Response.Body = originalStream;
            if (exception != null)
            {
                logEntityInfo.StatusCode = context.Response.StatusCode;
                logEntityInfo.Body = $"Request failed. An exception has happened. Status code: {logEntityInfo.StatusCode}";
                logEntityInfo.Response = TheaResponse.Fail(logEntityInfo.StatusCode, exception.ToString()).ToJson();
            }
            logEntityInfo.Elapsed = (int)DateTime.Now.Subtract(logEntityInfo.CreatedAt).TotalMilliseconds;
            
            logEntityInfo.Headers = context.Request.Headers.ToJson();
            if (context.Request.Headers.TryGetValue("Authorization", out var authorization))
            {
                logEntityInfo.Authorization = authorization.ToString();
                if (context.User != null)
                {
                    var passport = context.User.ToPassport();
                    logEntityInfo.UserId = passport.UserId;
                    logEntityInfo.UserName = passport.UserName;
                    logEntityInfo.TenantId = passport.TenantId;
                }
            }
            await context.Response.WriteAsync(logEntityInfo.Response);
            this.logger.LogEntity(logEntityInfo);
        }
    }
    private async Task<LogEntity> CreateLogEntity(HttpContext context)
    {
        var logEntityInfo = new LogEntity { Id = ObjectId.NewId(), AppId = this.appId, LogLevel = (int)LogLevel.Information };
        if (context.Request.Headers.TryGetValue("TraceId", out var traceIds))
        {
            var traceId = traceIds.ToString();
            context.TraceIdentifier = traceId;
            logEntityInfo.TraceId = traceId;
        }
        else
        {
            logEntityInfo.TraceId = ObjectId.NewId();
            context.TraceIdentifier = logEntityInfo.TraceId;
            context.Request.Headers.TryAdd("TraceId", new StringValues(logEntityInfo.TraceId));
        }
        if (context.Request.Headers.TryGetValue("Tag", out var tag))
            logEntityInfo.Tag = tag.ToString();

        logEntityInfo.Host = GetHost();
        logEntityInfo.ApiType = this.GetApiType(context.Request.Method);
        logEntityInfo.ClientIp = context.GetClientIp();
        var request = context.Request;
        var apiUrl = $"{request.Scheme}://*{request.Path}{request.QueryString}";
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
                    logEntityInfo.Parameters = $"QueryString: {HttpUtility.UrlDecode(context.Request.QueryString.ToString())} \nBody: ";
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
                            return ip.Address.ToString();
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