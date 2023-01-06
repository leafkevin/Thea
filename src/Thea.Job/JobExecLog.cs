using System;

namespace Thea.Job;

public enum JobStatus : byte
{
    Ready = 0,
    Executing = 1,
    Completed = 2,
    Fault = 3,
    Skipped = 4
}
public class JobExecLog
{
    public string LogId { get; set; }
    public string JobId { get; set; }
    public string AppId { get; set; }
    public string SchedId { get; set; }
    public bool IsTempFired { get; set; }
    public DateTime SchedTime { get; set; }
    public DateTime FiredTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Host { get; set; }
    public JobStatus Result { get; set; }
    public int Code { get; set; }
    public string Message { get; set; }
    public int RetryTimes { get; set; }
    public string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}
