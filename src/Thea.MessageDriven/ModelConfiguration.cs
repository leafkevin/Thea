using Trolley;

namespace Thea.MessageDriven;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Binding>(f => f.ToTable("mds_binding"))
            .Entity<Cluster>(f => f.ToTable("mds_cluster"))
            .Entity<ExecLog>(f => f.ToTable("mds_exec_log"))
            .UseAutoMap();
    }
}
