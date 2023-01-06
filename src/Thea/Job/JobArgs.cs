using System;

namespace Thea.Job;

public class JobArgs
{
    public string JobId { get; set; }
    public string SchedId { get; set; }
    public bool IsTempFired { get; set; }
    public DateTimeOffset FireTime { get; set; }
    public override string ToString()
    {
        return $"JobId:{this.JobId},FireTime:{this.FireTime:yyyy-MM-dd HH:mm:ss.fff},IsTempFired:{this.IsTempFired}";
    }
}