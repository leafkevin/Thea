using Trolley;

namespace Thea.MessageDriven.MySqlRepository;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Queue>(f => f.ToTable("mds_queue"))
            .Entity<Binding>(f => f.ToTable("mds_binding"))
            .Entity<ExecLog>(f => f.ToTable("mds_log"));
    }
}