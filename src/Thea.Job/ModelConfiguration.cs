using Thea.Orm;

namespace Thea.Job;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<JobDetail>(f =>
        {
            f.ToTable("jss_job").Key("JobId");
            f.Member(f => f.IsLocal).Ignore();
        });
    }
}