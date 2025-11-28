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
    private readonly List<string> skipUrls;

    public TheaWebMiddleware(RequestDelegate next, IConfiguration configuration, IResponseDecorator responseDecorator, ILogger<TheaWebMiddleware> logger)
    {
        this.appId = configuration.GetValue<string>("AppId");
        if (string.IsNullOrEmpty(this.appId))
            throw new ArgumentNullException("AppId is required in configuration.");

        this.skipUrls = configuration.GetSection("WebApi:SkipUrls").Get<List<string>>();
        if (this.skipUrls != null && this.skipUrls.Count > 0)
            this.skipUrls = this.skipUrls.ConvertAll(u => u.ToLower());
        this.next = next;
        this.responseDecorator = responseDecorator;
        this.logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var url = context.Request.Path.Value?.ToLower();
        if (this.skipUrls != null && this.skipUrls.Count > 0 && this.skipUrls.Contains(url))
        {
            await this.next(context);
            return;
        }
        var logLevel = LogLevel.Information;
        var originalStream = context.Response.Body;
        var logEntityInfo = await this.CreateLogEntity(context);
        using (this.logger.BeginScope(logEntityInfo))
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
                logLevel = LogLevel.Error;
            }
            logEntityInfo.StatusCode = context.Response.StatusCode;
            bool isJson = context.Response.ContentType?.ToLower().Contains("application/json") ?? true;
            if (isJson)
            {
                (logLevel, var response) = await this.responseDecorator.ProcessRequest(context, memoryStream, logLevel, exception);
                logEntityInfo.Response = response;
                context.Response.Body = originalStream;
                await context.Response.WriteAsync(response);
            }
            else
            {
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalStream);
                context.Response.Body = originalStream;
            }
            logEntityInfo.LogLevel = (int)logLevel;
            if (string.IsNullOrEmpty(logEntityInfo.Tag))
                logEntityInfo.Tag = "TheaWebMiddleware";
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
        }
        else
        {
            logEntityInfo.TraceId = ObjectId.NewId();
            context.TraceIdentifier = logEntityInfo.TraceId;
            context.Request.Headers.TryAdd("TraceId", new StringValues(logEntityInfo.TraceId));
        }

        logEntityInfo.Host = GetHost();
        logEntityInfo.ApiType = this.GetApiType(context.Request.Method);
        logEntityInfo.ClientIp = context.GetClientIp();
        var request = context.Request;
        var apiUrl = $"{request.Scheme}://*{request.Path}{request.QueryString}";
        logEntityInfo.ApiUrl = HttpUtility.UrlDecode(apiUrl);

        context.Request.EnableBuffering();
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey("TraceId"))
                context.Response.Headers.Append("TraceId", logEntityInfo.TraceId);
            return Task.CompletedTask;
        });
        switch (logEntityInfo.ApiType)
        {
            case (int)ApiType.HttpGet:
            case (int)ApiType.HttpDelete:
            case (int)ApiType.HttpPost:
            case (int)ApiType.HttpPut:
                logEntityInfo.Request = await this.ReadBody(context.Request.Body);
                break;
        }
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
        return logEntityInfo;
    }
    private async Task<string> ReadBody(Stream stream)
    {
        try
        {
            if (stream == null) return string.Empty;
            stream.Position = 0;
            var reader = new StreamReader(stream, leaveOpen: true);
            var result = await reader.ReadToEndAsync();
            stream.Position = 0;
            return result;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
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