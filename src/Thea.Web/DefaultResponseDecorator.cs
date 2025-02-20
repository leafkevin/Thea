using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Thea.Web;

class DefaultResponseDecorator : IResponseDecorator
{
    public async Task<string> ProcessRequest(HttpContext context, Stream readableStream, Exception exception)
    {
        string jsonResponse = null;
        TheaResponse response = null;
        var statusCode = context.Response.StatusCode;
        switch (statusCode)
        {
            case 400:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "请求地址未找到！");
                break;
            case 401:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "未授权！");
                break;
            case 403:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "禁止访问！");
                break;
            case 404:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "内部服务器错误！");
                break;
            case 500:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "内部服务器错误！");
                break;
            case 502:
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json;charset=utf-8";
                response = TheaResponse.Fail(statusCode, "网关错误！");
                break;
            case 200:
                jsonResponse = await this.ReadBody(readableStream);
                if (!string.IsNullOrEmpty(jsonResponse))
                    response = jsonResponse.JsonTo<TheaResponse>();
                else response = TheaResponse.Success;
                break;
        }
        if (exception != null)
        {
            context.Response.Clear();
            context.Response.StatusCode = 200;
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
        return response.ToJson();
    }
    private async Task<string> ReadBody(Stream stream)
    {
        stream.Position = 0;
        var reader = new StreamReader(stream);
        var result = await reader.ReadToEndAsync();
        stream.Position = 0;
        return result;
    }
}