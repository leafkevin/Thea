using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Thea.Logging;

namespace Thea.Job;

class JobScheduler
{
    private static TimeSpan FireDelay = TimeSpan.FromSeconds(30);
    private readonly Task task;
    private readonly CancellationTokenSource stopTokenSource = new();
    private readonly EventWaitHandle readyToStart = new EventWaitHandle(false, EventResetMode.AutoReset);
    private readonly ConcurrentDictionary<string, JobDetail> jobDetails = new();
    private readonly ConcurrentDictionary<string, JobTrigger> jobTriggers = new();
    private readonly ConcurrentQueue<JobMessage> messageQueue = new();
    private readonly JobService parent;
    private readonly ILogger<JobScheduler> logger;
    private DateTimeOffset timeAfter = DateTimeOffset.MinValue;
    private DateTimeOffset deadline = DateTimeOffset.MinValue;
    private DateTime lastInitedTime = DateTime.MinValue;
    private DateTime lastUpdatedTime = DateTime.MinValue;
    private DateTime lastCheckedTime = DateTime.MinValue;
    private string AppId { get; set; }
    public string NodeId { get; set; }
    public string DbKey { get; set; }
    public List<JobDetail> JobDetails => this.jobDetails.Values.ToList();

    public JobScheduler(JobService parent, IServiceProvider serviceProvider)
    {
        this.parent = parent;
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        this.logger = loggerFactory.CreateLogger<JobScheduler>();
        this.task = Task.Factory.StartNew(() =>
        {
            this.readyToStart.WaitOne();

            this.timeAfter = DateTimeOffset.Now.ToHalfMinute();
            this.deadline = this.timeAfter.Add(FireDelay);

            while (!this.stopTokenSource.IsCancellationRequested)
            {
                try
                {
                    if (DateTime.Now - this.lastInitedTime > TimeSpan.FromMinutes(1))
                    {
                        this.Initialize();
                        this.lastInitedTime = DateTime.Now;
                    }

                    //提前5秒钟完成初始化和触发工作
                    if (DateTimeOffset.Now + TimeSpan.FromSeconds(5) > this.timeAfter)
                    {
                        foreach (var jobTrigger in this.jobTriggers.Values)
                        {
                            if (jobTrigger.CanNextFire(this.timeAfter, this.deadline, out var jobArgs))
                            {
                                this.parent.TryProcessMessage(new JobMessage
                                {
                                    MessageType = JobMessageType.AddShedule,
                                    Body = jobArgs
                                });
                            }
                        }
                        this.timeAfter = this.timeAfter.Add(FireDelay);
                        this.deadline = this.deadline.Add(FireDelay);

                        //每15分钟执行一次，检查30分钟内，漏执行的job，并报警出来
                        //当前调度触发完后，有30秒的时间可以执行检查
                        //if (DateTime.Now - this.lastCheckedTime > TimeSpan.FromMinutes(15))
                        //{
                        //    var endTime = this.timeAfter.AddMinutes(-1);
                        //    var startTime = endTime.AddMinutes(-30);
                        //    await this.CheckMissFiredJob(startTime, endTime);
                        //    this.lastCheckedTime = DateTime.Now;
                        //}
                    }
                    if (DateTimeOffset.Now + TimeSpan.FromSeconds(10) < this.timeAfter)
                    {
                        if (this.messageQueue.TryDequeue(out var message))
                        {
                            switch (message.MessageType)
                            {
                                case JobMessageType.UpdateJob:
                                    var jobDetail = message.Body as JobDetail;
                                    this.jobDetails[jobDetail.JobId] = jobDetail;
                                    this.AdjustCronExpr();
                                    break;
                                case JobMessageType.RemoveJobWorker:
                                    var jobId = message.Body as string;
                                    this.jobDetails.TryRemove(jobId, out _);
                                    this.jobTriggers.TryRemove(jobId, out _);
                                    break;
                            }
                        }
                        if (DateTime.Now - this.lastUpdatedTime > TimeSpan.FromSeconds(5))
                        {
                            this.parent.TryProcessMessage(new JobMessage
                            {
                                MessageType = JobMessageType.ExecToDb
                            });
                            this.lastUpdatedTime = DateTime.Now;
                        }
                    }
                    if (DateTimeOffset.Now + TimeSpan.FromSeconds(5) < this.timeAfter)
                        Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    this.logger.LogTagError("JobScheduler", ex, ex.ToString());
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
    public void RegisterWorker(string cronExpr, IJobWorker jobWorker)
    {
        var workerType = jobWorker.GetType();
        var jobId = jobWorker.JobId;
        var jobDetail = new JobDetail
        {
            JobId = jobId,
            JobName = jobId,
            AppId = this.AppId,
            TypeName = workerType.FullName,
            CronExpr = cronExpr,
            AdjustedCronExpr = cronExpr,
            IsEnabled = true,
            CreatedBy = this.NodeId,
            UpdatedBy = this.NodeId,
            IsLocal = true
        };
        this.jobDetails.AddOrUpdate(jobId, jobDetail, (k, o) => jobDetail);
    }
    private void Initialize()
    {
        var curJobDetails = this.jobDetails.Values.ToList();
        var existedJobs = this.parent.TryProcessMessage(new JobMessage
        {
            MessageType = JobMessageType.RegisterJob,
            Body = curJobDetails
        }) as List<JobDetail>;
        if (existedJobs != null && existedJobs.Count > 0)
        {
            foreach (var jobDetail in existedJobs)
            {
                if (!this.jobDetails.TryGetValue(jobDetail.JobId, out var localJobDetail))
                {
                    if (!jobDetail.IsEnabled) continue;
                }
                if (!jobDetail.IsEnabled)
                {
                    //删除对应的JobTrigger和JobWorker
                    this.jobDetails.TryRemove(jobDetail.JobId, out _);
                    this.jobTriggers.TryRemove(jobDetail.JobId, out _);
                    this.parent.TryProcessMessage(new JobMessage
                    {
                        MessageType = JobMessageType.RemoveJobWorker,
                        Body = jobDetail.JobId
                    });
                }
                //更新JobDetail
                if (jobDetail.UpdatedAt > localJobDetail.UpdatedAt)
                {
                    //删除没有Cron表达式的Job
                    if (string.IsNullOrEmpty(jobDetail.CronExpr))
                    {
                        //删除对应的JobTrigger和JobWorker
                        this.jobDetails.TryRemove(jobDetail.JobId, out _);
                        this.jobTriggers.TryRemove(jobDetail.JobId, out _);
                        this.parent.TryProcessMessage(new JobMessage
                        {
                            MessageType = JobMessageType.RemoveJobWorker,
                            Body = jobDetail.JobId
                        });
                        continue;
                    }
                    if (jobDetail.CronExpr != localJobDetail.CronExpr
                        || jobDetail.IsAllowAdjust && string.IsNullOrEmpty(jobDetail.AdjustedCronExpr))
                    {
                        this.messageQueue.Enqueue(new JobMessage
                        {
                            MessageType = JobMessageType.UpdateJob,
                            Body = jobDetail,
                        });
                    }
                }
            }
        }
    }
    private void AdjustCronExpr()
    {
        var adjustJobDetails = this.jobDetails.Values.Where(f => f.IsAllowAdjust && f.CronExpr != null).ToList();
        if (adjustJobDetails == null || adjustJobDetails.Count <= 0)
            return;

        //按执行频度，把Job分配到intervalList列表中，高于60秒的频度按照60秒计算
        var intervalList = new Dictionary<int, List<JobDetail>>();
        foreach (var jobDetail in adjustJobDetails)
        {
            var interval = this.GetMinInterval(jobDetail.CronExpr);
            if (!intervalList.TryGetValue(interval, out var jobList))
            {
                intervalList.TryAdd(interval, jobList = new List<JobDetail>());//   n/{interval}
            }
            jobList.Add(jobDetail);
        }
        //把频度按照从小到大排序
        var sortedIntervalList = intervalList.Keys.ToList();
        sortedIntervalList.Sort((x, y) => x.CompareTo(y));
        //在1-60秒内，把所有的Job使用的次数都记录下来
        var secondList = new Dictionary<int, int>();
        for (int i = 0; i < 60; i++)
        {
            secondList.Add(i, 0);
        }
        //按照1-60秒内，使用次数进行排列这些Job,优先分配使用次数少的秒
        //Job触发执行的频度不变，只是改变执行的起始秒，把0-59秒分配给这些Job
        foreach (var interval in sortedIntervalList)
        {
            //从频度快到频度慢进行分配，每分配一次都是平分60秒
            //被分配的秒已被使用，就从下1秒开始，继续找空闲的秒进行分配
            //如果没有空闲的，就从使用次数较少的秒进行分配
            var step = intervalList[interval].Count % 60;
            step = (int)Math.Ceiling(60 / (double)step);
            int idleSecondIndex = 0, index = 0;
            intervalList[interval].Sort((x, y) => x.JobId.CompareTo(y.JobId));
            foreach (var jobDetail in intervalList[interval])
            {
                idleSecondIndex = this.GetIdleSecondIndex(secondList, interval, idleSecondIndex);
                jobDetail.AdjustedCronExpr = this.GetCronExpr(jobDetail.CronExpr, idleSecondIndex, interval);
                //更新JobDetail的Cron表达式，更新JobTrigger的Cron表达式
                if (this.jobTriggers.TryGetValue(jobDetail.JobId, out var jobTrigger))
                    jobTrigger.Build(jobDetail.AdjustedCronExpr);

                //计算2分钟内，执行的秒，如：
                //Cron:3/20 * * * * ?
                //执行的秒:3,23,43秒，各执行2次
                var count = 120 / interval;
                for (int i = 0; i < count; i++)
                {
                    var idleSecond = (idleSecondIndex + i * interval) % 60;
                    secondList[idleSecond]++;
                }

                idleSecondIndex += step;
                if (idleSecondIndex >= 60)
                {
                    idleSecondIndex %= 60;
                    step = (int)Math.Ceiling(60 / (double)(intervalList[interval].Count - index - 1));
                }
                index++;
            }
        }
    }
    private int GetIdleSecondIndex(Dictionary<int, int> secondList, int interval, int loopIndex)
    {
        var minSecond = secondList.Min(f => f.Value);
        var chosedList = secondList.Where(f => f.Value == minSecond).Select(f => f.Key).OrderBy(f => f).ToList();

        var times = 120 / interval;
        int index = 0, result = chosedList[0];
        for (int i = 0; i < times; i++)
        {
            if (chosedList[index] < loopIndex)
            {
                i = 0;
                index++;
                continue;
            }
            var second = (chosedList[index] + i * interval) % 60;
            if (!chosedList.Contains(second))
            {
                i = 0;
                index++;
            }
        }
        return chosedList[index];
    }
    private string GetCronExpr(string cronExpr, int idleSecond, int interval)
    {
        var endIndex = 1;
        for (int i = 0; i < cronExpr.Length; i++)
        {
            if (char.IsWhiteSpace(cronExpr[i]))
            {
                endIndex = i;
                break;
            }
        }
        if (interval < 60)
            return $"{idleSecond}/{interval}" + cronExpr.Substring(endIndex);

        return $"{idleSecond}" + cronExpr.Substring(endIndex);
    }
    private int GetMinInterval(string cronExpr)
    {
        var numExpr = string.Empty;
        char lastSymbol = '0';
        int commaCount = 0, index = 0, result = 0;
        for (int i = 0; i < cronExpr.Length; i++)
        {
            switch (cronExpr[i])
            {
                case ',':
                    commaCount++;
                    lastSymbol = ',';
                    break;
                case '/':
                case '-':
                case '*':
                    lastSymbol = cronExpr[i];
                    break;
                default:
                    if (Char.IsWhiteSpace(cronExpr[i]))
                    {
                        var interval = 0;
                        switch (lastSymbol)
                        {
                            case ',': interval = 60 / ++commaCount; break;
                            case '/': interval = int.Parse(numExpr); break;
                            case '-':
                            case '*': interval = 1; break;
                            default: interval = 60; break;
                        }
                        if (index > 0 && interval > 1)
                            result = 60;
                        else result = interval;
                        if (index > 0) return result;
                    }
                    if (Char.IsDigit(cronExpr[i]))
                    {
                        if (lastSymbol == '/') numExpr += cronExpr[i];
                    }
                    index++;
                    break;
            }
        }
        return result;
    }
}
