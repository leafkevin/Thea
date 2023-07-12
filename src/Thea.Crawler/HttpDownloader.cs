using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Thea.Crawler;

public class HttpDownloader : IDisposable
{
    private readonly IHttpClientFactory clientFactory;
    private HttpRequestMessage reqMessage;
    private HttpClientHandler clientHandler;

    public HttpDownloader(IHttpClientFactory clientFactory)
    {
        this.clientFactory = clientFactory;
    }
    public HttpDownloader Create(string url, HttpMethod method, Action<HttpRequestHeaders> headersInitializer = null)
    {
        this.reqMessage = new HttpRequestMessage(method, url);
        headersInitializer?.Invoke(this.reqMessage.Headers);
        return this;
    }
    public HttpDownloader WithContent(Func<HttpContent> contentInitializer = null)
    {
        if (contentInitializer != null)
            this.reqMessage.Content = contentInitializer();
        return this;
    }
    public HttpDownloader UseProxy(string proxyAddress, string user = null, string password = null)
    {
        if (string.IsNullOrEmpty(proxyAddress))
            throw new ArgumentNullException(nameof(proxyAddress));

        var webProxy = new WebProxy(proxyAddress);
        if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
            webProxy.Credentials = new NetworkCredential(user, password);
        this.clientHandler = new HttpClientHandler
        {
            UseCookies = true,
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            Proxy = webProxy
        };
        return this;
    }
    public async Task<string> DownloadAsync()
    {
        try
        {
            using var client = this.clientFactory.CreateClient();
            if (this.reqMessage.Content == null)
            {
                this.reqMessage.Content = new StringContent(string.Empty);
                //this.reqMessage.Content.Headers.Add("Content-Type", "text/html");
            }

            var resp = await client.SendAsync(this.reqMessage);
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(), Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"download {this.reqMessage.RequestUri} is failed: {ex}");
        }
        return null;
    }
    public void Dispose()
    {
        if (this.reqMessage != null)
        {
            this.reqMessage.Dispose();
            this.reqMessage = null;
        }
    }
}

