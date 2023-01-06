namespace Thea.Job;

enum JobMessageType : byte
{
    RegisterJob = 1,
    AddShedule = 2,
    ExecuteJob = 3,
    UpdateJob = 4,
    RemoveJobWorker = 5,
    StartSchedLog = 6,
    EndSchedLog = 7,
    AddSchedLog = 8,
    ExecToDb = 9
}
class JobMessage
{
    public JobMessageType MessageType { get; set; }
    public object Body { get; set; }
}
