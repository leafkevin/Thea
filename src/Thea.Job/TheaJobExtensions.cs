using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Reflection;
using Thea.Orm;

namespace Thea.Job
{
    public static class TheaJobExtensions
    {
        public static IServiceCollection AddJob(this IServiceCollection services, Action<JobSchedulerBuilder> optionsInitializer)
        {
            services.AddSingleton<IJobService>(f =>
           {
               var jobService = new JobService(f);
               var dbFactory = f.GetService<IOrmDbFactory>();
               new ModelConfiguration().OnModelCreating(new ModelBuilder(dbFactory));
               var options = new JobSchedulerBuilder(jobService);
               optionsInitializer?.Invoke(options);
               return jobService;
           });
            return services;
        }
        public static DateTimeOffset ToHalfMinute(this DateTimeOffset dateTime)
        {
            if (dateTime.Second > 30)
            {
                return dateTime.AddSeconds(30 - dateTime.Second).AddMilliseconds(-dateTime.Millisecond);
            }
            return dateTime.AddSeconds(-dateTime.Second).AddMilliseconds(-dateTime.Millisecond);
        }
        internal static SortedSet<int> TailSet(this SortedSet<int> set, int value)
        {
            return set.GetViewBetween(value, 9999999);
        }
    }
    public class JobSchedulerBuilder
    {
        private readonly JobService jobService;
        internal JobSchedulerBuilder(JobService jobService)
            => this.jobService = jobService;
        public JobSchedulerBuilder SetNodeId(string nodeId)
        {
            this.jobService.NodeId = nodeId;
            var envNodeId = Environment.GetEnvironmentVariable("NodeId");
            if (!string.IsNullOrEmpty(envNodeId))
                this.jobService.NodeId = nodeId;
            return this;
        }
        public JobSchedulerBuilder SetDbKey(string dbKey)
        {
            this.jobService.DbKey = dbKey;
            return this;
        }
        public void RegisterFrom(Assembly assembly) => this.jobService.RegisterFrom(assembly);
        public void Register<TJobWorker>(string cronExpr) where TJobWorker : IJobWorker
            => this.jobService.Register<TJobWorker>(cronExpr);
    }
}
