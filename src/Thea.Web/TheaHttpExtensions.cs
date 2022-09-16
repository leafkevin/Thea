using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
    public static async Task<HttpContent> GetAsync(this HttpClient client, string url, Action<HttpRequestMessage> initializer = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, url);
        initializer?.Invoke(message);
        var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return response.Content;
    }
    public static async Task<TResponse> GetAsync<TResponse>(this HttpClient client, string url, Action<HttpRequestMessage> initializer = null)
    {
        var content = await GetAsync(client, url, initializer);
        var json = await content.ReadAsStringAsync();
        return json.JsonTo<TResponse>();
    }
    public static async Task<HttpContent> PostAsync(this HttpClient client, string url, object parameters, Action<HttpContent> initializer = null)
    {
        var content = new StringContent(parameters.ToJson(), Encoding.UTF8);
        initializer?.Invoke(content);
        var response = await client.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
        return response.Content;
    }
    public static async Task<TResponse> PostAsync<TResponse>(this HttpClient client, string url, object parameters, Action<HttpContent> initializer = null)
    {
        var content = await PostAsync(client, url, parameters, initializer);
        var json = await content.ReadAsStringAsync();
        return json.JsonTo<TResponse>();
    }
    public static async Task<HttpContent> PutAsync(this HttpClient client, string url, object parameters, Action<HttpContent> initializer = null)
    {
        var content = new StringContent(parameters.ToJson(), Encoding.UTF8);
        initializer?.Invoke(content);
        var response = await client.PutAsync(url, content);
        response.EnsureSuccessStatusCode();
        return response.Content;
    }
    public static async Task<HttpContent> DeleteAsync(this HttpClient client, string url, Action<HttpRequestMessage> initializer = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Delete, url);
        initializer?.Invoke(message);
        var response = await client.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return response.Content;
    }
    public static async Task DownloadAsync(this HttpClient client, string url, string savePath)
    {
        using var stream = await client.GetStreamAsync(url);
        using var fs = new FileStream(savePath, FileMode.Create);

        byte[] bArr = new byte[1024];
        int size = stream.Read(bArr, 0, (int)bArr.Length);
        while (size > 0)
        {
            fs.Write(bArr, 0, size);
            size = stream.Read(bArr, 0, (int)bArr.Length);
        }
        fs.Flush();
        stream.Close();
    }
    public static async Task<TheaResponse> ResponseTo<TResult>(this HttpContent content)
    {
        var json = await content.ReadAsStringAsync();
        return TheaResponse.Succeed(json.JsonTo<TResult>());
    }
    public static string GetToken(this IHttpContextAccessor contextAccessor)
    {
        var curRequest = contextAccessor.HttpContext.Request;
        if (curRequest.Headers.TryGetValue("Authorization", out var token))
        {
            return token.ToString().Replace("Bearer ", String.Empty);
        }
        return String.Empty;
    }
}
