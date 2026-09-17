using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Thea.MessageDriven;

namespace Thea.Alarm;

public class MessageDrivenAlarmService : IAlarmService
{
    private readonly IMessageDriven messageDriven;
    private readonly bool isEnabled;
    public MessageDrivenAlarmService(IMessageDriven messageDriven, IConfiguration configuration)
    {
        this.isEnabled = configuration.GetValue("Alarm:IsEnabled", true);
        this.messageDriven = messageDriven;
    }
    public async Task PostAsync(AlarmRequest request)
    {
        if (!this.isEnabled) return;
        await this.messageDriven.PublishAsync("alarm", "#", request);
    }
    public string Escape(string message) => message;
}