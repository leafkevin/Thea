using System.Threading.Tasks;

namespace Thea.Job;

public interface IJobWorker
{
    string JobId { get; }
    Task Execute(JobArgs args);
}