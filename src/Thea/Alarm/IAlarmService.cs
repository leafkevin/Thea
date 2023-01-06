using System.Threading.Tasks;

namespace Thea.Alarm;

public interface IAlarmService
{
    Task PostAsync(string sceneKey, string title, string content);
}

