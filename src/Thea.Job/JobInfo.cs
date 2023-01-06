namespace Thea.Job;

class JobInfo
{
    public JobDetail  JobDetail { get; set; }
    public IJobWorker JobWorker { get; set; }
    public JobTrigger Trigger { get; set; }
}

