namespace Thea.Alarm;

public interface IAlarmService
{
    void PostAlarm(string sceneKey, string title, string content);
}
