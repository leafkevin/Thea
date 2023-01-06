using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Thea.Job;

class JobService : IJobService
{
    private readonly JobScheduler jobScheduler;
    private readonly JobExecutor jobExecutor;
    private readonly JobRepository jobRepository;
    public string AppId { get; set; }
    public string NodeId { get; set; }
    public string DbKey { get; set; }

    public JobService(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        this.AppId = configuration.GetValue<string>("AppId");
        if (string.IsNullOrEmpty(this.AppId))
            throw new ArgumentException("appsetting.json文件未配置AppId");

        this.jobExecutor = new JobExecutor(this, serviceProvider);
        this.jobScheduler = new JobScheduler(this, serviceProvider);
        this.jobRepository = new JobRepository(this, serviceProvider);
    }
    public void Execute(JobArgs jobArgs)
    {
        this.jobExecutor.TryProcessMessage(new JobMessage
        {
            MessageType = JobMessageType.ExecuteJob,
            Body = jobArgs
        });
    }
    public void UpdateJob(string jobId)
    {
        var jobDetail = this.jobRepository.GetJob(jobId);
        if (jobDetail == null)
        {
            this.jobScheduler.TryProcessMessage(new JobMessage
            {
                MessageType = JobMessageType.RemoveJobWorker,
                Body = jobId
            });
            this.jobExecutor.TryProcessMessage(new JobMessage
            {
                MessageType = JobMessageType.RemoveJobWorker,
                Body = jobId
            });
            return;
        }
        this.jobScheduler.TryProcessMessage(new JobMessage
        {
            MessageType = JobMessageType.UpdateJob,
            Body = jobDetail
        });
        var adjuestList = this.jobScheduler.JobDetails;
        this.jobRepository.AdjustCronExpr(adjuestList);
    }
    public void RegisterFrom(Assembly assembly)
    {
        var jobWorkerTypes = assembly.GetTypes().Where(f => typeof(IJobWorker).IsAssignableFrom(f) && !f.IsAbstract && !f.IsInterface).ToList();
        jobWorkerTypes.ForEach(f => this.jobExecutor.RegisterWorker(f));
    }
    public void Register<TJobWorker>(string cronExpr) where TJobWorker : IJobWorker
    {
        var workerType = typeof(TJobWorker);
        var jobWorker = this.jobExecutor.RegisterWorker(workerType);
        this.jobScheduler.RegisterWorker(cronExpr, jobWorker);
    }
    public object TryProcessMessage(JobMessage message)
    {
        object result = null;
        switch (message.MessageType)
        {
            case JobMessageType.RegisterJob:
                var jobDetails = message.Body as List<JobDetail>;
                result = this.jobRepository.RegisterJob(this.AppId, jobDetails);
                break;
            case JobMessageType.StartSchedLog:
                this.jobRepository.StartSchedLog(message.Body as JobExecLog);
                break;
            case JobMessageType.EndSchedLog:
                this.jobRepository.EndSchedLog(message.Body as JobExecLog);
                break;
            case JobMessageType.AddSchedLog:
                this.jobRepository.AddSchedLog(message.Body as JobExecLog);
                break;
            case JobMessageType.ExecToDb:
                this.jobRepository.Execute();
                break;
            case JobMessageType.AddShedule:
                this.jobExecutor.TryProcessMessage(message);
                break;
            case JobMessageType.RemoveJobWorker:
                this.jobExecutor.TryProcessMessage(message);
                break;
        }
        return result;
    }
}
