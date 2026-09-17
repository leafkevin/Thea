using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Thea.Web;

class DefaultResponseDecorator : IResponseDecorator
{
    public async Task<(LogLevel, string)> ProcessRequest(HttpContext context, Stream readableStream, LogLevel logLevel, Exception exception)
    {
        var myLogLevel = logLevel;
        string jsonResponse = null;
        TheaResponse response = null;
        var statusCode = context.Response.StatusCode;
        switch (statusCode)
        {
            case 400:
                myLogLevel = LogLevel.Warning;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "请求地址未找到！");
                break;
            case 401:
                myLogLevel = LogLevel.Warning;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "未授权！");
                break;
            case 403:
                myLogLevel = LogLevel.Warning;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "禁止访问！");
                break;
            case 404:
                myLogLevel = LogLevel.Warning;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "内部服务器错误！");
                break;
            case 500:
                myLogLevel = LogLevel.Warning;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "内部服务器错误！");
                break;
            case 502:
                myLogLevel = LogLevel.Warning;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "网关错误！");
                break;
            case 200:
                jsonResponse = await readableStream.ReadBody();
                if (!string.IsNullOrEmpty(jsonResponse))
                    response = jsonResponse.JsonTo<TheaResponse>();
                else Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}, TraceId: {context.GetTraceId()}, Warning: Response body is empty.");

                if (response != null && !response.IsSuccess && myLogLevel < LogLevel.Warning)
                    myLogLevel = LogLevel.Warning;
                break;
        }
        if (exception != null)
        {
            context.Response.Clear();
            context.Response.ContentType = "application/json;charset=utf-8";
            context.Response.OnStarting(state =>
            {
                var response = (HttpResponse)state;
                response.Headers[HeaderNames.CacheControl] = "no-cache";
                response.Headers[HeaderNames.Pragma] = "no-cache";
                response.Headers[HeaderNames.Expires] = "-1";
                response.Headers.Remove(HeaderNames.ETag);
                return Task.CompletedTask;
            }, context.Response);
            response = TheaResponse.Fail(statusCode, "内部服务器错误！");
        }
        return (myLogLevel, response.ToJson());
    }
}