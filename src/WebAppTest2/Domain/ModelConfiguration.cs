using Trolley;

namespace WebAppTest2.Domain;

class ModelConfiguration : DefaultModelMappingConfiguration
{
    public override void OnModelCreating(ModelBuilder builder)
    {
        //builder
        //    .Entity<Cluster>(f => f.ToTable("mds_cluster"))
        //    .Entity<Binding>(f => f.ToTable("mds_binding"))
        //    .Entity<ExecLog>(f => f.ToTable("mds_log"));
    }
}