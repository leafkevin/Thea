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
                var options = new JobSchedulerBuilder(jobService);
                optionsInitializer?.Invoke(options);
                var dbFactory = f.GetService<IOrmDbFactory>();
                dbFactory.Configure(options.OrmProviderType, new ModelConfiguration());
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
        public Type OrmProviderType { get; private set; }
        internal JobSchedulerBuilder(JobService jobService)
            => this.jobService = jobService;
        public JobSchedulerBuilder SetHostName(string HostName)
        {
            this.jobService.HostName = HostName;
            var envHostName = Environment.GetEnvironmentVariable("HostName");
            if (!string.IsNullOrEmpty(envHostName))
                this.jobService.HostName = HostName;
            return this;
        }
        public JobSchedulerBuilder SetOrmProvider<TOrmProvider>(string dbKey) where TOrmProvider : IOrmProvider
        {
            this.jobService.DbKey = dbKey;
            this.OrmProviderType = typeof(TOrmProvider);
            return this;
        }
        public void RegisterFrom(Assembly assembly) => this.jobService.RegisterFrom(assembly);
        public void Register<TJobWorker>(string cronExpr) where TJobWorker : IJobWorker
            => this.jobService.Register<TJobWorker>(cronExpr);
    }
}
