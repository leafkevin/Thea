using Trolley;

namespace Thea.MessageDriven.MySqlRepository;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Cluster>(f => f.ToTable("mds_cluster"))
            .Entity<ExecLog>(f => f.ToTable("mds_log"));
    }
}