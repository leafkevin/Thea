using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private readonly ILogger<DingtalkAlarmService> logger;
    private readonly IHttpClientFactory clientFactory;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, AlarmRequest>> requests = new();
    private readonly Dictionary<string, DingtalkChannel> channels = new();
    private readonly bool isEnabled;
    private readonly string apiUrl;

    public DingtalkAlarmService(IConfiguration configuration, IHttpClientFactory clientFactory, ILogger<DingtalkAlarmService> logger)
    {
        this.isEnabled = configuration.GetValue("Alarm:IsEnabled", true);
        this.clientFactory = clientFactory;
        this.apiUrl = configuration.GetValue("Alarm:Url", "https://oapi.dingtalk.com/robot/send?access_token=");
        var myChannels = configuration.GetSection("Alarm:Channels").Get<List<AlarmChannel<DingtalkChannel>>>();
        if (myChannels == null || myChannels.Count == 0)
            throw new ArgumentNullException("appsettings.json中缺少配置项Alarm:Channels");
        myChannels = myChannels.FindAll(f => f.Type == "Dingtalk");
        if (myChannels == null || myChannels.Count == 0)
            throw new ArgumentNullException("appsettings.json中缺少配置项Alarm:Channels，且至少包含一个Type为Dingtalk的Channel");

        foreach (var channel in myChannels)
        {
            this.channels.TryAdd(channel.ChannelId, channel.Value);
        }
    }
    public async Task PostAsync(AlarmRequest request)
    {
        try
        {
            if (!this.isEnabled) return;
            var myAppRequests = this.requests.GetOrAdd(request.AppId, f => new());
            if (!myAppRequests.TryGetValue(request.SenceKey, out var myAlarmRequest))
            {
                myAppRequests.TryAdd(request.SenceKey, myAlarmRequest = request);
                myAlarmRequest.FiredTimes = 1;
                await this.PublishMessage(myAlarmRequest);
            }
            else myAlarmRequest.FiredTimes++;

            //十分钟后再报一次，并删除报警信息，防止占用太多内存
            var removeKeys = new List<int>();
            foreach (var key in myAppRequests.Keys)
            {
                var alarmRequest = myAppRequests[key];
                if (DateTime.UtcNow.Subtract(alarmRequest.CreatedAt) > TimeSpan.FromMinutes(10))
                {
                    if (alarmRequest.FiredTimes > 1)
                        await this.PublishMessage(alarmRequest);
                    removeKeys.Add(key);
                }
            }
            if (removeKeys.Count > 0)
                removeKeys.ForEach(key => myAppRequests.TryRemove(key, out _));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"content: {request.ToJson()}, Exception Detail: {ex}");
        }
    }
    public string Escape(string message)
        => message.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "'");
    private async Task PublishMessage(AlarmRequest request)
    {
        try
        {
            if (!this.channels.TryGetValue(request.ChannelId, out var channelInfo))
                throw new KeyNotFoundException($"ChannelId {request.ChannelId} is not configured.");
            var message = this.BuildMessage(request);
            var signValue = this.Sign(channelInfo.Secret, out var timestamp);
            var url = $"{apiUrl}{channelInfo.Token}&timestamp={timestamp}&sign={signValue}";
            var httpContent = new StringContent(message, Encoding.UTF8);
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
            Console.WriteLine($"content: {request.Content.ToJson()}, PublishMessage Exception Detail: {ex}");
        }
    }
    private string BuildMessage(AlarmRequest request)
    {
        var builder = new StringBuilder();
        builder.Append("{\"msgtype\": \"markdown\",");
        builder.Append("\"markdown\":{\"title\":\"");
        builder.Append(request.Title);
        builder.Append("\",\"text\":\"");
        foreach (var item in request.Content)
        {
            builder.Append($"`{item.Key}`：{this.Escape(item.Value)}");
        }
        builder.Append($"\n\n`触发次数`：{request.FiredTimes} ");
        builder.Append("\"},\"at\":{\"isAtAll\":true}}");
        return builder.ToString();
    }
    private string Sign(string secret, out long timestamp)
    {
        timestamp = (DateTime.UtcNow.Ticks - 621355968000000000) / 10000;
        var signKey = timestamp + "\n" + secret;
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
    class DingtalkChannel
    {
        public string Token { get; set; }
        public string Secret { get; set; }
    }
}