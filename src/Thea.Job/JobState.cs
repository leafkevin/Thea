using System;

namespace Thea.Job
{
    class JobState
    {
        public string JobId { get; set; }
        public string SchedId { get; set; }
        public DateTime SchedTime { get; set; }
        public JobStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
