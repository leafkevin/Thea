using System.Threading.Tasks;

namespace Thea.Alarm;

public interface IAlarmService
{
    Task PostAsync(string title, string content);
    string Escape(string message);
}