using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.Job;

class JobExecutor
{
    private readonly Task task;
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly CancellationTokenSource stopTokenSource = new();
    private readonly ConcurrentDictionary<string, IJobWorker> jobWorkers = new();
    private readonly ConcurrentDictionary<string, JobState> jobStates = new();
    private readonly ConcurrentQueue<JobMessage> messageQueue = new();
    private readonly List<JobArgs> readyList = new();
    private readonly IServiceProvider serviceProvider;
    private readonly JobService parent;
    private readonly ILogger<JobExecutor> logger;

    public string AppId { get; private set; }
    public string HostName { get; private set; }
    public JobExecutor(JobService parent, IServiceProvider serviceProvider)
    {
        this.parent = parent;
        this.serviceProvider = serviceProvider;
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        this.logger = loggerFactory.CreateLogger<JobExecutor>();
        this.AppId = parent.AppId;
        this.HostName = parent.HostName;

        this.task = Task.Factory.StartNew(() =>
        {
            this.readyToStart.WaitOne();

            while (!this.stopTokenSource.IsCancellationRequested)
            {
                try
                {
                    while (this.readyList.Count > 0)
                    {
                        var jobArgs = this.readyList[0];
                        //提前2秒准备执行,为了支持更多的job准时执行
                        if (DateTimeOffset.Now + TimeSpan.FromSeconds(2) >= jobArgs.FireTime)
                        {
                            if (this.jobWorkers.TryGetValue(jobArgs.JobId, out var worker))
                            {
                                Task.Run(async () => await this.Execute(worker, jobArgs));
                                this.readyList.RemoveAt(0);
                            }
                        }
                        else break;
                    }
                    if (DateTimeOffset.Now + TimeSpan.FromSeconds(5) >= this.readyList[0].FireTime
                        && this.messageQueue.TryDequeue(out var message))
                    {
                        switch (message.MessageType)
                        {
                            case JobMessageType.RemoveJobWorker:
                                var jobId = message.Body as string;
                                this.jobWorkers.TryRemove(jobId, out _);
                                break;
                            case JobMessageType.AddShedule:
                                var jobs = message.Body as List<JobArgs>;
                                this.readyList.AddRange(jobs);
                                this.readyList.Sort((x, y) => x.FireTime.CompareTo(y.FireTime));
                                break;
                            case JobMessageType.ExecuteJob:
                                var jobArgs = message.Body as JobArgs;
                                if (this.jobWorkers.TryGetValue(jobArgs.JobId, out var worker))
                                    Task.Run(async () => await this.Execute(worker, jobArgs));
                                break;
                        }
                    }
                    else Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    this.logger.LogTagError("JobExecutor", ex, $"An exception occurred during JobExecutor execution, error message: {ex.Message}");
                }
            }
        }, this.stopTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Start() => this.readyToStart.Set();
    public void Shutdown()
    {
        this.stopTokenSource.Cancel();
        if (this.task != null) this.task.Wait();
        this.readyToStart.Dispose();
        this.stopTokenSource.Dispose();
    }
    public void TryProcessMessage(JobMessage message) => this.messageQueue.Enqueue(message);
    public IJobWorker RegisterWorker(Type jobWorkerType)
    {
        var jobWorker = TheaActivator.CreateInstance(this.serviceProvider, jobWorkerType) as IJobWorker;
        this.jobWorkers.AddOrUpdate(jobWorker.JobId, jobWorker, (k, o) => jobWorker);
        return jobWorker;
    }
    private async Task Execute(IJobWorker worker, JobArgs jobArgs)
    {
        var schedTime = jobArgs.IsTempFired ? DateTime.Now : jobArgs.FireTime.ToLocalTime().DateTime;
        var logInfo = new JobExecLog
        {
            LogId = ObjectId.NewId(),
            JobId = jobArgs.JobId,
            AppId = this.AppId,
            IsTempFired = jobArgs.IsTempFired,
            SchedId = jobArgs.SchedId,
            SchedTime = schedTime,
            Host = this.HostName,
            Result = JobStatus.Ready,
            RetryTimes = 0,
            CreatedBy = this.HostName,
            UpdatedBy = this.HostName,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        if (this.jobStates.TryGetValue(jobArgs.JobId, out var jobState))
        {
            //必须在下次触发的前2秒内结束，否则将会被跳过执行
            if (jobState.Status <= JobStatus.Completed)
            {
                logInfo.Result = JobStatus.Skipped;
                logInfo.FiredTime = DateTime.Now;
                logInfo.EndTime = DateTime.Now;
                var elapsed = DateTime.Now - jobState.CreatedAt;
                logInfo.Message = $"任务JobId[{jobArgs.JobId}],上次调度[{jobState.SchedTime:yyyy-MM-dd HH:mm:ss}]未执行完毕，已耗时{elapsed.TotalMilliseconds}ms，本次调度[{jobArgs.FireTime:yyyy-MM-dd HH:mm:ss}]已跳过！";

                var jobScheduler = this.serviceProvider.GetService<JobScheduler>();
                jobScheduler.TryProcessMessage(new JobMessage
                {
                    MessageType = JobMessageType.AddSchedLog,
                    Body = logInfo
                });
                //写日志，同时也会触发报警
                this.logger.LogEntity(new LogEntity
                {
                    Id = ObjectId.NewId(),
                    LogLevel = LogLevel.Warning,
                    AppId = this.parent.AppId,
                    ApiType = ApiType.LocalInvoke,
                    Tag = "JobExecutor",
                    Body = logInfo.Message,
                    Parameters = $"JobId:[{jobArgs.JobId}],{jobState.SchedTime:yyyy-MM-dd HH:mm:ss}",
                    StatusCode = 500,
                    CreatedAt = jobState.CreatedAt
                });
                return;
            }
        }
        else
        {
            this.jobStates.TryAdd(jobArgs.JobId, new JobState
            {
                JobId = jobArgs.JobId,
                SchedId = jobArgs.SchedId,
                SchedTime = jobArgs.FireTime.ToLocalTime().DateTime,
                Status = JobStatus.Executing,
                CreatedAt = DateTime.Now
            });
            this.parent.TryProcessMessage(new JobMessage
            {
                MessageType = JobMessageType.StartSchedLog,
                Body = logInfo
            });
        }

        if (!jobArgs.IsTempFired)
        {
            //等待准时触发
            while (jobArgs.FireTime - DateTimeOffset.UtcNow > TimeSpan.Zero)
            {
                if (jobArgs.FireTime - DateTimeOffset.UtcNow <= TimeSpan.Zero) break;
                if (jobArgs.FireTime - DateTimeOffset.UtcNow > TimeSpan.FromMilliseconds(50))
                    Thread.Sleep(10);
            }
        }
        logInfo.FiredTime = DateTime.Now;

        //执行Job,失败重试3次
        var times = 3;
        Exception exception = null;
        while (times > 0)
        {
            try
            {
                await worker.Execute(jobArgs);
                logInfo.Result = JobStatus.Completed;
                break;
            }
            catch (Exception ex)
            {
                exception = ex;
                logInfo.Result = JobStatus.Fault;
                logInfo.Code = -1;
                logInfo.Message = ex.ToString();
            }
            times--;
            logInfo.RetryTimes++;
        }
        if (logInfo.Result == JobStatus.Fault)
        {
            //写日志，同时也会触发报警
            this.logger.LogEntity(new LogEntity
            {
                Id = ObjectId.NewId(),
                AppId = this.parent.AppId,
                LogLevel = LogLevel.Error,
                Tag = "JobExecutor",
                Body = $"任务JobId:{jobArgs.JobId}, FireTime:{logInfo.SchedTime.ToString("yyyy-MM-dd HH:mm:ss")} 执行失败,已重试{logInfo.RetryTimes}次。{Environment.NewLine}{exception}",
                Exception = exception,
                Parameters = jobArgs.ToString(),

                StatusCode = 500,
                CreatedAt = logInfo.FiredTime,
            });
        }

        logInfo.EndTime = DateTime.Now;
        logInfo.UpdatedBy = GetIpAddress() + "-" + Thread.CurrentThread.ManagedThreadId.ToString();
        logInfo.UpdatedAt = DateTime.Now;

        this.parent.TryProcessMessage(new JobMessage
        {
            MessageType = JobMessageType.EndSchedLog,
            Body = logInfo
        });
        this.jobStates.TryRemove(jobArgs.JobId, out var removed);
    }
    private static string GetIpAddress()
    {
        foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (item.NetworkInterfaceType == NetworkInterfaceType.Ethernet && item.OperationalStatus == OperationalStatus.Up)
            {
                var properties = item.GetIPProperties();
                if (properties.GatewayAddresses.Count > 0)
                {
                    foreach (var ip in properties.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return ip.Address.ToString();
                        }
                    }
                }
            }
        }
        return string.Empty;
    }
}
