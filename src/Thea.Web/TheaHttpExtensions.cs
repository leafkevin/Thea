using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Thea.Web;

public static class TheaHttpExtensions
{
    public static IHttpClientBuilder AddTraceId(this IHttpClientBuilder builder)
    {
        builder.AddHttpMessageHandler<TheaHttpMessageHandler>();
        return builder;
    }
    public static HttpClient CreateClient(this IHttpClientFactory clientFactory, string name = null, int timeoutSeconds = 30)
    {
        var httpClient = clientFactory.CreateClient(name);
        httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return httpClient;
    }
    public static HttpClient CreateClient(this IHttpClientFactory clientFactory, string proxyAddress, string name = null, string proxyUser = null, string proxyPassword = null, bool isIgnoreSslError = true, int timeoutSeconds = 30)
    {
        var theaClientFactory = clientFactory as TheaHttpClientFactory;
        if (theaClientFactory == null)
            throw new Exception("请注册IHttpClientFactory默认实现类为TheaHttpClientFactory");

        var httpClient = theaClientFactory.CreateClient(name, proxyAddress, proxyUser, proxyPassword, isIgnoreSslError);
        httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return httpClient;
    }
    public static void WithToken(this HttpContent content, string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentNullException(nameof(token));
        content.Headers.Add("Authorization", $"Bearer {token}");
    }
    public static void WithToken(this HttpRequestMessage message, string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ArgumentNullException(nameof(token));
        message.Headers.Add("Authorization", $"Bearer {token}");
    }
    public static void WithMediaType(this HttpContent content, string mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
            throw new ArgumentNullException(nameof(mediaType));
        content.Headers.ContentType = new MediaTypeWithQualityHeaderValue(mediaType);
    }
    public static string GetTraceId(this IHttpContextAccessor contextAccessor)
    {
        if (contextAccessor == null)
            return null;
        return contextAccessor.HttpContext.GetTraceId();
    }
    public static string GetTraceId(this HttpContext context)
    {
        if (context == null)
            return null;
        if (context.Request.Headers.TryGetValue("TraceId", out var traceId))
            return traceId.ToString();
        return context.TraceIdentifier.Replace(":", "-");
    }
    public static string GetClientIp(this HttpContext context)
    {
        if (context == null)
            return null;
        return context.Request.GetClientIp();
    }
    public static string GetClientIp(this HttpRequest request, bool isContainsProxy = false)
    {
        string result = null;
        if (isContainsProxy)
        {
            if (TryGetHeaderValue(request, "X-Forwarded-For", out result)) return result;
            else if (TryGetHeaderValue(request, "X-Real-IP", out result)) return result;
            else if (TryGetHeaderValue(request, "CF-Connecting-IP", out result)) return result;
            else if (TryGetHeaderValue(request, "HTTP_X_FORWARDED_FOR", out result)) return result;
            else if (TryGetHeaderValue(request, "REMOTE_ADDR", out result)) return result;
            else if (TryGetHeaderValue(request, "X-Original-For", out result)) return result;
            else if (TryGetHeaderValue(request, "Proxy-Client-IP", out result)) return result;
            else if (TryGetHeaderValue(request, "WL-Proxy-Client-IP", out result)) return result;
            else if (TryGetHeaderValue(request, "HTTP_CLIENT_IP", out result)) return result;
            else if (TryGetHeaderValue(request, "HTTP_X_FORWARDED_FOR", out result)) return result;
        }
        return request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
    }
    private static bool TryGetHeaderValue(HttpRequest request, string key, out string result)
    {
        if (!request.Headers.ContainsKey(key))
        {
            result = null;
            return false;
        }
        var headerValue = request.Headers[key].ToString();
        headerValue = headerValue.Replace("::ffff:", string.Empty);
        headerValue = headerValue.Replace("unknown", string.Empty);
        if (string.IsNullOrEmpty(headerValue))
        {
            result = null;
            return false;
        }
        result = headerValue;
        return true;
    }
}
