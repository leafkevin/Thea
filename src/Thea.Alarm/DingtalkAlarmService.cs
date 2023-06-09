using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;

namespace Thea.Alarm;

class DingtalkAlarmService : IAlarmService
{
    private static readonly MediaTypeHeaderValue ApplicationJson = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
    private const string ApiUrl = "https://oapi.dingtalk.com/robot/send?access_token=";
    private readonly ILogger<DingtalkAlarmService> logger;
    private readonly IHttpClientFactory clientFactory;
    private readonly string token;
    private readonly string secret;

    public DingtalkAlarmService(IConfiguration configuration, IHttpClientFactory clientFactory, ILogger<DingtalkAlarmService> logger)
    {
        this.clientFactory = clientFactory;
        this.token = configuration.GetValue("Alarm:Token", string.Empty);
        this.secret = configuration.GetValue("Alarm:Secret", string.Empty);
        if (string.IsNullOrEmpty(token))
            throw new Exception("配置项Alarm:Token不能为null");
        this.logger = logger;
    }
    public async Task PostAsync(string sceneKey, string title, string content)
    {
        try
        {
            var builder = new StringBuilder();
            content = content.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "'");
            builder.Append("{\"msgtype\": \"markdown\",");
            builder.Append("\"markdown\":{\"title\":\"");
            builder.Append(title);
            builder.Append("\",\"text\":\"");
            builder.Append(content);
            builder.Append("\"},\"at\":{\"isAtAll\":true}}");
            var body = builder.ToString();
            var signValue = this.Sign(out var timestamp);
            var url = $"{ApiUrl}{this.token}&timestamp={timestamp}&sign={signValue}";
            var httpContent = new StringContent(body, Encoding.UTF8);
            httpContent.Headers.ContentType = ApplicationJson;
            using var client = this.clientFactory.CreateClient();
            var response = await client.PostAsync(url, httpContent);
            response.EnsureSuccessStatusCode();
            var jsonResult = await response.Content.ReadAsStringAsync();
            var result = jsonResult.JsonTo<DingtalkResult>();
            if (!result.IsSuccess)
                this.logger.LogError($"post dingtalk alarm, return error. code:{result.Code}, message:{result.Message}");
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, $"post dingtalk url error,detail:{ex}");
        }
    }
    private string Sign(out long timestamp)
    {
        timestamp = (DateTime.Now.ToUniversalTime().Ticks - 621355968000000000) / 10000;
        var signKey = timestamp + "\n" + this.secret;
        byte[] keyByte = Encoding.UTF8.GetBytes(secret);
        byte[] contentBytes = Encoding.UTF8.GetBytes(signKey);
        using var hmacsha256 = new HMACSHA256(keyByte);
        contentBytes = hmacsha256.ComputeHash(contentBytes);
        var signValue = Convert.ToBase64String(contentBytes);
        return HttpUtility.UrlEncode(signValue, Encoding.UTF8);
    }
    class DingtalkResult
    {
        public bool IsSuccess => this.Code == 0;
        [JsonPropertyName("errcode")]
        public int Code { get; set; }
        [JsonPropertyName("errmsg")]
        public string Message { get; set; }
    }
}
