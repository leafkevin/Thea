using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thea.Alarm;

public interface IAlarmService
{
    Task PostAsync(AlarmRequest request);
    string Escape(string message);
}
public class AlarmRequest
{
    public string AppId { get; set; }
    public string ChannelId { get; set; }
    public int Level { get; set; }
    public int SenceKey { get; set; }
    public string Title { get; set; }
    public List<KeyValuePair<string, string>> Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public int FiredTimes { get; set; }
}
public interface IAlarmImageConverter
{
    byte[] Create(AlarmRequest request);
}