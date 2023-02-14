using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Thea.Alarm;

class DingtalkAlarmService : IAlarmService
{
    private static readonly MediaTypeHeaderValue ApplicationJson = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
    private const string ApiUrl = "https://oapi.dingtalk.com/robot/send?access_token=";
    private readonly ILogger<DingtalkAlarmService> logger;
    private readonly IHttpClientFactory clientFactory;
    private readonly string webhookToken;

    public DingtalkAlarmService(IConfiguration configuration, IHttpClientFactory clientFactory, ILogger<DingtalkAlarmService> logger)
    {
        this.clientFactory = clientFactory;
        this.webhookToken = configuration.GetValue("Alarm:WebhookToken", string.Empty);
        if (string.IsNullOrEmpty(webhookToken))
            throw new Exception("配置项Alarm:WebhookToken不能为null");
        this.logger = logger;
    }
    public async Task PostAsync(string sceneKey, string title, string content)
    {
        try
        {
            var builder = new StringBuilder();
            builder.Append("{\"msgtype\": \"markdown\",");
            builder.Append("\"markdown\":{\"title\":\"");
            builder.Append(title);
            builder.Append("\",\"text\":\"");
            builder.Append(content.Replace("\"", "\\\\\"").Replace("`", "'"));
            builder.Append("\"},\"at\"{\"isAtAll\":true}");
            var body = builder.ToString();

            var httpContent = new StringContent(body, Encoding.UTF8);
            httpContent.Headers.ContentType = ApplicationJson;
            using var client = this.clientFactory.CreateClient();
            var url = ApiUrl + this.webhookToken;
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
}
class DingtalkResult
{
    public bool IsSuccess => this.Code == 0;
    [JsonPropertyName("errcode")]
    public int Code { get; set; }
    [JsonPropertyName("errmsg")]
    public string Message { get; set; }
}