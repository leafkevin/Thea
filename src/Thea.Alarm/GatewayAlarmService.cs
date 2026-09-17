using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Thea.Alarm;

public class GatewayAlarmService : IAlarmService
{
    private readonly IServiceProvider serviceProvider;
    private readonly List<string> channelIds;
    private readonly bool isEnabled;

    public GatewayAlarmService(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        this.serviceProvider = serviceProvider;
        this.isEnabled = configuration.GetValue("Alarm:IsEnabled", true);
        var myChannels = configuration.GetSection("Alarm:Channels").Get<List<AlarmChannel>>();
        if (myChannels == null || myChannels.Count == 0)
            throw new ArgumentNullException("appsettings.json中缺少配置项Alarm:Channels");
        this.channelIds = myChannels.Select(f => f.ChannelId).ToList();
    }
    public async Task PostAsync(AlarmRequest request)
    {
        if (!this.isEnabled) return;
        if (!this.channelIds.Contains(request.ChannelId))
            throw new KeyNotFoundException($"ChannelId {request.ChannelId} is not configured.");

        var lowAlarmService = this.serviceProvider.GetKeyedService<IAlarmService>(typeof(TelegramAlarmService));
        await lowAlarmService.PostAsync(request);
        if (request.Level > 0)
        {
            request.ChannelId = "Wechat";
            var highAlarmService = this.serviceProvider.GetKeyedService<IAlarmService>(typeof(WechatAlarmService));
            await highAlarmService.PostAsync(request);
        }
    }
    public string Escape(string message) => message;
}