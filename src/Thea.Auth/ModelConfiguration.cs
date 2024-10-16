using Trolley;

namespace Thea.Auth;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Resource>(f => f.ToTable("sys_resource"))
            .Entity<Permission>(f => f.ToTable("sys_permission"))
            .UseAutoMap();
    }
}
