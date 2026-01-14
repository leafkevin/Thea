using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Web;
using Thea.Logging;

namespace Thea.Web;

public class TheaWebMiddleware
{
    private readonly string appId;
    private readonly RequestDelegate next;
    private readonly bool isDevelopment;
    private readonly IResponseDecorator responseDecorator;
    private readonly ILogger<TheaWebMiddleware> logger;
    private readonly List<string> skipUrls;

    public TheaWebMiddleware(RequestDelegate next, IConfiguration configuration,
        IHostEnvironment environment, IResponseDecorator responseDecorator, ILogger<TheaWebMiddleware> logger)
    {
        this.appId = configuration.GetValue<string>("AppId");
        if (string.IsNullOrEmpty(this.appId))
            throw new ArgumentNullException("AppId is required in configuration.");

        this.skipUrls = configuration.GetSection("WebApi:SkipUrls").Get<List<string>>();
        if (this.skipUrls != null && this.skipUrls.Count > 0)
            this.skipUrls = this.skipUrls.ConvertAll(u => u.ToLower());
        this.isDevelopment = environment.IsDevelopment();
        this.next = next;
        this.responseDecorator = responseDecorator;
        this.logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var traceId = this.CreateTraceId(context);
        var url = context.Request.Path.Value?.ToLower();
        try
        {
            if (this.skipUrls != null && this.skipUrls.Count > 0 && this.skipUrls.Contains(url))
            {
                await this.next(context);
                return;
            }
            var logLevel = LogLevel.Information;
            var originalStream = context.Response.Body;
            var logEntityInfo = await this.CreateLogEntity(traceId, context);
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
                    if (this.isDevelopment) Console.WriteLine(ex.ToString());
                }
                if (string.IsNullOrEmpty(logEntityInfo.Tag))
                    logEntityInfo.Tag = "Thea";
                logEntityInfo.StatusCode = context.Response.StatusCode;

                if (context.RequestAborted.IsCancellationRequested)
                {
                    logLevel = LogLevel.Error;
                    logEntityInfo.Body = "Client cancelled the request.";
                    context.Response.Body = originalStream;
                    logEntityInfo.LogLevel = (int)logLevel;
                    this.logger.LogEntity(logEntityInfo);
                    return;
                }
                if (!logEntityInfo.IsEnabled || ScopeState.TryGetState(out var lastScopeState) && !lastScopeState.IsEnabled)
                {
                    memoryStream.Position = 0;
                    await memoryStream.CopyToAsync(originalStream);
                    context.Response.Body = originalStream;
                    return;
                }

                bool isJson = context.Response.ContentType?.ToLower().Contains("application/json") ?? true;
                if (isJson)
                {
                    (logLevel, var response) = await this.responseDecorator.ProcessRequest(context, memoryStream, logLevel, exception);
                    logEntityInfo.Response = response;
                    context.Response.Body = originalStream;
                    if (!string.IsNullOrEmpty(response))
                        await context.Response.WriteAsync(response);
                }
                else
                {
                    memoryStream.Position = 0;
                    await memoryStream.CopyToAsync(originalStream);
                    context.Response.Body = originalStream;
                }
            }
            logEntityInfo.LogLevel = (int)logLevel;
            this.logger.LogEntity(logEntityInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}, TraceId:{traceId}, 请求{url}异常，Details: {ex}");
        }
    }
    private string CreateTraceId(HttpContext context)
    {
        string traceId = null;
        if (context.Request.Headers.TryGetValue("TraceId", out var traceIds))
            traceId = traceIds.ToString();
        else traceId = ObjectId.NewId();
        return traceId;
    }
    private async Task<LogEntity> CreateLogEntity(string traceId, HttpContext context)
    {
        var logEntityInfo = new LogEntity { Id = ObjectId.NewId(), TraceId = traceId };
        context.TraceIdentifier = traceId;
        context.Request.Headers.TryAdd("TraceId", new StringValues(traceId));

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