using System;

namespace Thea.Job;

public class JobDetail
{
    public string JobId { get; set; }
    public string JobName { get; set; }
    public string AppId { get; set; }
    public string TypeName { get; set; }
    public string CronExpr { get; set; }
    public string AdjustedCronExpr { get; set; }
    public bool IsAllowAdjust { get; set; }
    public bool IsEnabled { get; set; }
    public string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsLocal { get; set; }
}
