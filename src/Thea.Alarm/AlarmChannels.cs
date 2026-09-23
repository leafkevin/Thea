namespace Thea.Alarm;

class AlarmChannel
{
    public string ChannelId { get; set; }
    public string Type { get; set; }
    public object Value { get; set; }
}
class AlarmChannel<T> : AlarmChannel
{
    public new T Value
    {
        get { return (T)base.Value; }
        set { base.Value = value; }
    }
}