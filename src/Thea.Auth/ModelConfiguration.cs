using Trolley;

namespace Thea.Auth;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Resource>(f => f.ToTable("sys_resource"))
            .Entity<RoleResource>(f => f.ToTable("sys_role_resource"))
            .UseAutoMap();
    }
}
