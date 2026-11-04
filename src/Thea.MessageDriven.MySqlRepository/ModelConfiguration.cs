using Trolley;

namespace Thea.MessageDriven.MySqlRepository;

class ModelConfiguration : DefaultModelMappingConfiguration
{
    public override void OnModelCreating(ModelBuilder builder)
    {
        builder
            .Entity<Binding>(f => f.ToTable("mds_binding"))
            .Entity<Queue>(f => f.ToTable("mds_queue"))
            .Entity<ExecLog>(f => f.ToTable("mds_log"));
    }
}