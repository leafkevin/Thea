using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Thea.Alarm;

public class TelegramAlarmService : IAlarmService
{
    private readonly Dictionary<string, TelegramBotClient> alarmBots = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, AlarmRequest>> requests = new();
    private readonly Dictionary<string, TelegramChannel> channels = new();
    private readonly bool isEnabled;

    public TelegramAlarmService(IConfiguration configuration, IHttpClientFactory clientFactory)
    {
        this.isEnabled = configuration.GetValue("Alarm:IsEnabled", true);
        var httpProxy = configuration.GetValue<string>("HttpProxy");
        var myChannels = configuration.GetSection("Alarm:Channels").Get<List<AlarmChannel>>();
        if (myChannels == null || myChannels.Count == 0)
            throw new ArgumentNullException("appsettings.json中缺少配置项Alarm:Channels");
        myChannels = myChannels.FindAll(f => f.Type == "Telegram");
        if (myChannels == null || myChannels.Count == 0)
            throw new ArgumentNullException("appsettings.json中缺少配置项Alarm:Channels，且至少包含一个Type为Telegram的Channel");

        HttpClient httpClient = null;
        if (!string.IsNullOrEmpty(httpProxy))
            httpClient = clientFactory.CreateClient("ProxyClient");
        foreach (var channel in myChannels)
        {
            var channelInfo = channel.Value.JsonTo<TelegramChannel>();
            this.channels.TryAdd(channel.ChannelId, channelInfo);
            if (this.alarmBots.ContainsKey(channelInfo.Token))
                continue;
            var botClient = new TelegramBotClient(channelInfo.Token, httpClient);
            this.alarmBots.TryAdd(channelInfo.Token, botClient);
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
    {
        if (string.IsNullOrEmpty(message)) return message;
        var chars = new[] { "_", "*", "[", "]", "(", ")", "~", "`", ">", "#", "+", "-", "=", "|", "{", "}", ".", "!" };
        foreach (var c in chars)
            message = message.Replace(c, "\\" + c);
        return message;
    }
    private async Task PublishMessage(AlarmRequest request)
    {
        try
        {
            if (!this.channels.TryGetValue(request.ChannelId, out var channelInfo))
                throw new KeyNotFoundException($"ChannelId {request.ChannelId} is not configured.");
            if (!this.alarmBots.TryGetValue(channelInfo.Token, out var botClient))
                throw new KeyNotFoundException($"Token {channelInfo.Token} is not configured.");

            var builder = new StringBuilder();
            builder.AppendLine($"*{this.Escape(request.Title)}*");
            foreach (var item in request.Content)
            {
                builder.AppendLine($"`{item.Key}`：{this.Escape(item.Value)}");
            }
            builder.AppendLine($"`触发次数`：{request.FiredTimes}");
            int index = 0;
            var message = builder.ToString();
            var chatId = new ChatId(channelInfo.ChatId);
            while (index < message.Length)
            {
                var length = Math.Min(4096, message.Length - index);
                if (length == 4096)
                {
                    var lastChar = message.Substring(index + length - 1, 1);
                    while (lastChar == "\\")
                    {
                        length--;
                        lastChar = message.Substring(index + length - 1, 1);
                    }
                }
                var myMessage = message.Substring(index, length);
                await botClient.SendMessage(chatId, myMessage, ParseMode.MarkdownV2);
                index += length;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"content: {request.Content.ToJson()}, PublishMessage Exception Detail: {ex}");
        }
    }
    class TelegramChannel
    {
        public long ChatId { get; set; }
        public string Token { get; set; }
    }
}