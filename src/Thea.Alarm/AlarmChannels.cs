namespace Thea.Alarm;

class AlarmChannel
{
    public string ChannelId { get; set; }
    public string Type { get; set; }
    public object Value { get; set; }
}
class HttpPusherChannel
{
    public string PushUrl { get; set; }
}