using Trolley;
using WebAppTest.Domain.Models;

namespace WebAppTest.Domain;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<User>(f => f.ToTable("sys_user"))
            .Entity<Role>(f => f.ToTable("sys_role"))
            .Entity<UserRole>(f => f.ToTable("sys_user_role"))
            .Entity<Resource>(f => f.ToTable("sys_resource"))
            .Entity<Authorization>(f => f.ToTable("sys_authorization"))
            .Entity<Group>(f => f.ToTable("sys_group"))
            .Entity<Data>(f => f.ToTable("sys_data"))
            .Entity<UserGroup>(f => f.ToTable("sys_user_group"))
            .Entity<Lookup>(f => f.ToTable("sys_lookup"))
            .Entity<LookupValue>(f => f.ToTable("sys_lookup_value").Member(f => f.Value).Field("lookup_value"))
            .Entity<Cluster>(f => f.ToTable("mds_cluster"))
            .Entity<Binding>(f => f.ToTable("mds_binding"))
            .Entity<ExecLog>(f => f.ToTable("mds_log"))
            .Entity<Rule>(f => f.ToTable("res_rule"))
            .Entity<RuleParameter>(f => f.ToTable("res_rule_parameter"))
            .UseAutoMap();
    }
}