using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Thea.Logging;
using Thea.Orm;

namespace Thea.Job;

class JobRepository
{
    private readonly JobService parent;
    private readonly IOrmDbFactory dbFactory;
    private readonly StringBuilder sqlBuilder = new();
    private readonly ILogger<JobRepository> logger;
    public string DbKey { get; private set; }
    public JobRepository(JobService parent, IServiceProvider serviceProvider)
    {
        this.parent = parent;
        this.DbKey = parent.DbKey;
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        this.logger = loggerFactory.CreateLogger<JobRepository>();
        this.dbFactory = serviceProvider.GetService<IOrmDbFactory>();
    }
    public List<JobDetail> RegisterJob(string appId, List<JobDetail> jobDetails)
    {
        using var repository = this.dbFactory.Create(this.DbKey);
        var existedJobs = repository.Query<JobDetail>(f => f.AppId == appId);
        var newJobs = jobDetails.FindAll(t => !existedJobs.Exists(f => f.JobId == t.JobId));
        if (newJobs.Count > 0)
        {
            newJobs.ForEach(f =>
            {
                f.CreatedAt = DateTime.Now;
                f.UpdatedAt = DateTime.Now;
            });
            repository.Create<JobDetail>(newJobs);
        }
        return existedJobs;
    }
    public JobDetail GetJob(string jobId)
    {
        using var repository = this.dbFactory.Create(this.DbKey);
        return repository.Get<JobDetail>(new { JobId = jobId });
    }
    public void AdjustCronExpr(List<JobDetail> jobDetails)
    {
        using var repository = this.dbFactory.Create(this.DbKey);
        repository.Update<JobDetail>().WithBy(f => new { f.AdjustedCronExpr, f.UpdatedAt }, jobDetails).Execute();
    }
    public void StartSchedLog(JobExecLog execLog)
    {
        var schedTime = execLog.SchedTime.ToString("yyyy-MM-dd HH:mm:ss");
        var host = String.IsNullOrEmpty(execLog.Host) ? "NULL" : $"'{execLog.Host}'";
        var createdAt = execLog.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var updatedAt = execLog.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
        this.sqlBuilder.Append("INSERT INTO jss_exec_log(LogId,JobId,AppId,IsTempFired,SchedId,SchedTime,Host,Result,CreatedBy,CreatedAt,UpdatedBy,UpdatedAt) ");
        this.sqlBuilder.Append($"VALUES ('{execLog.LogId}','{execLog.JobId}','{execLog.AppId}',{execLog.IsTempFired},'{execLog.SchedId}','{schedTime}',{host},{(byte)execLog.Result},'{execLog.UpdatedBy}','{createdAt}','{execLog.UpdatedBy}','{updatedAt}');");
    }
    public void EndSchedLog(JobExecLog execLog)
    {
        var firedTime = execLog.FiredTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var endTime = execLog.EndTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var message = this.Transfer(execLog.Message);
        var updatedAt = execLog.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
        this.sqlBuilder.Append($"UPDATE jss_exec_log SET FiredTime='{firedTime}',EndTime='{endTime}',RetryTimes={execLog.RetryTimes},Result={(byte)execLog.Result},Code={execLog.Code},Message={message},UpdatedBy='{execLog.UpdatedBy}',UpdatedAt='{updatedAt}' WHERE LogId='{execLog.LogId}';");
    }
    public void AddSchedLog(JobExecLog execLog)
    {
        var schedTime = execLog.SchedTime.ToString("yyyy-MM-dd HH:mm:ss");
        var firedTime = execLog.FiredTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var endTime = execLog.EndTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var message = this.Transfer(execLog.Message);
        var host = String.IsNullOrEmpty(execLog.Host) ? "NULL" : $"'{execLog.Host}'";
        var updatedAt = execLog.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff");
        this.sqlBuilder.Append("INSERT INTO jss_exec_log(LogId,JobId,AppId,IsTempFired,SchedId,SchedTime,FiredTime,EndTime,Host,Result,Code,Message,CreatedBy,CreatedAt,UpdatedBy,UpdatedAt)");
        this.sqlBuilder.Append($"VALUES ('{execLog.LogId}','{execLog.JobId}','{execLog.AppId}',{execLog.IsTempFired},'{execLog.SchedId}','{schedTime}','{firedTime}','{endTime}',{host},{(byte)execLog.Result},{execLog.Code},{message},'{execLog.UpdatedBy}','{updatedAt}','{execLog.UpdatedBy}','{updatedAt}');");
    }
    //public async Task<List<JobExecLog>> QuerySchedLog(string appId, DateTime beginTime, DateTime endTime)
    //{
    //    var sql = "SELECT JobId,SchedTime,Result FROM jss_exec_log WHERE AppId=@AppId AND SchedTime>=@BeginTime and SchedTime<@EndTime AND IsTempFired=0";
    //    using var repository = this.dbFactory.Create(this.dbKey);
    //    return await repository.QueryAsync<JobExecLog>(sql, new { AppId = appId, BeginTime = beginTime, EndTime = endTime });
    //}
    public bool Execute()
    {
        if (this.sqlBuilder.Length > 0)
        {
            var sql = this.sqlBuilder.ToString();
            try
            {
                using var repository = this.dbFactory.Create(this.DbKey);
                repository.Execute(sql);
            }
            catch (Exception ex)
            {
                this.logger.LogTagError("JobRepository", $"Execute sql error,{ex.Message},Sql:{sql}");
            }
            this.sqlBuilder.Clear();
            return true;
        }
        return false;
    }
    private string Transfer(string message)
    {
        if (!String.IsNullOrEmpty(message))
        {
            message = message.Replace("'", "''");
            message = message.Replace("\\", "\\\\");
            if (message.Length > 3800)
            {
                message = message.Substring(0, 3800);
            }
            message = "'" + message + "'";
            return message;
        }
        return "NULL";
    }
}
