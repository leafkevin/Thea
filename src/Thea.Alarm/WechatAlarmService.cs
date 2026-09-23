using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Thea.Json;

namespace Thea.Alarm;

public class WechatAlarmService : IAlarmService
{
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, AlarmRequest>> requests = new();
    private readonly Dictionary<string, string> channels = new();
    private readonly IAlarmImageConverter imageConverter;
    private readonly bool isEnabled;


    public WechatAlarmService(IConfiguration configuration, IHttpClientFactory clientFactory, IAlarmImageConverter imageConverter)
    {
        this.isEnabled = configuration.GetValue("Alarm:IsEnabled", true);
        this.httpClient = clientFactory.CreateClient();
        this.imageConverter = imageConverter;
        var myChannels = configuration.GetSection("Alarm:Channels").Get<List<AlarmChannel<HttpPusherChannel>>>();
        if (myChannels == null || myChannels.Count == 0)
            throw new ArgumentNullException("appsettings.json中缺少配置项Alarm:Channels");
        var httpChannels = myChannels.FindAll(f => f.Type == "Wechat");
        if (httpChannels == null || httpChannels.Count == 0)
            throw new ArgumentNullException("appsettings.json中缺少配置项Alarm:Channels，且至少包含一个Type为Wechat的Channel");

        foreach (var channel in myChannels)
        {
            this.channels[channel.ChannelId] = channel.Value.PushUrl;
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
    public string Escape(string message) => message;
    private async Task PublishMessage(AlarmRequest request)
    {
        if (!this.channels.TryGetValue(request.ChannelId, out var pushUrl))
            throw new KeyNotFoundException($"ChannelId {request.ChannelId} is not configured.");

        request.Content.Add(new KeyValuePair<string, string>("触发次数", request.FiredTimes.ToString()));
        var imageBytes = this.imageConverter.Create(request);
        var base64 = Convert.ToBase64String(imageBytes);
        var jsonContent = JsonContent.Create(new
        {
            msgtype = "image",
            image = new { base64, md5 = imageBytes.ToMd5() }
        }, options: TheaJsonSerializer.SerializerOptions);
        var respMessage = await this.httpClient.PostAsync(pushUrl, jsonContent);
        respMessage.EnsureSuccessStatusCode();
    }
    class HttpPusherChannel
    {
        public string PushUrl { get; set; }
    }
}
