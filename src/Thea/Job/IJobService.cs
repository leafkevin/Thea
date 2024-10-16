namespace Thea.Job;

public interface IJobService
{
    string HostName { get; set; }
    string DbKey { get; set; }
    void Execute(JobArgs args);
    void UpdateJob(string jobId);
}