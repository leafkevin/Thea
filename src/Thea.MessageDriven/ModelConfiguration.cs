using MySqlConnector;
using Thea.Orm;

namespace Thea.MessageDriven;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Cluster>(f =>
        {
            f.ToTable("mds_cluster").Key(t => t.ClusterId);
            f.Member(t => t.ClusterId).Field(nameof(Cluster.ClusterId)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.ClusterName).Field(nameof(Cluster.ClusterName)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.BindType).Field(nameof(Cluster.BindType)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.Url).Field(nameof(Cluster.Url)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.User).Field(nameof(Cluster.User)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.Password).Field(nameof(Cluster.Password)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.BindType).Field(nameof(Cluster.BindType)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.IsLogEnabled).Field(nameof(Cluster.IsLogEnabled)).NativeDbType(MySqlDbType.Bool);
            f.Member(t => t.IsEnabled).Field(nameof(Cluster.IsEnabled)).NativeDbType(MySqlDbType.Bool);
            f.Member(t => t.CreatedBy).Field(nameof(Cluster.CreatedBy)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.CreatedAt).Field(nameof(Cluster.CreatedAt)).NativeDbType(MySqlDbType.DateTime);
            f.Member(t => t.UpdatedBy).Field(nameof(Cluster.UpdatedBy)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.UpdatedAt).Field(nameof(Cluster.UpdatedAt)).NativeDbType(MySqlDbType.DateTime);
        });
        builder.Entity<Binding>(f =>
        {
            f.ToTable("mds_binding").Key(t => t.BindingId);
            f.Member(t => t.BindingId).Field(nameof(Binding.BindingId)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.ClusterId).Field(nameof(Binding.ClusterId)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.Exchange).Field(nameof(Binding.Exchange)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.Queue).Field(nameof(Binding.Queue)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.BindType).Field(nameof(Binding.BindType)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.BindingKey).Field(nameof(Binding.BindingKey)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.PrefetchCount).Field(nameof(Binding.PrefetchCount)).NativeDbType(MySqlDbType.Int32);
            f.Member(t => t.IsSingleActiveConsumer).Field(nameof(Binding.IsSingleActiveConsumer)).NativeDbType(MySqlDbType.Bool);
            f.Member(t => t.IsReply).Field(nameof(Binding.IsReply)).NativeDbType(MySqlDbType.Bool);
            f.Member(t => t.IsEnabled).Field(nameof(Binding.IsEnabled)).NativeDbType(MySqlDbType.Bool);
            f.Member(t => t.CreatedBy).Field(nameof(Binding.CreatedBy)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.CreatedAt).Field(nameof(Binding.CreatedAt)).NativeDbType(MySqlDbType.DateTime);
            f.Member(t => t.UpdatedBy).Field(nameof(Binding.UpdatedBy)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.UpdatedAt).Field(nameof(Binding.UpdatedAt)).NativeDbType(MySqlDbType.DateTime);
        });
        builder.Entity<ExecLog>(f =>
        {
            f.ToTable("mds_exec_log").Key(t => t.LogId);
            f.Member(t => t.LogId).Field(nameof(ExecLog.LogId)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.ClusterId).Field(nameof(ExecLog.ClusterId)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.RoutingKey).Field(nameof(ExecLog.RoutingKey)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.Body).Field(nameof(ExecLog.Body)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.IsSuccess).Field(nameof(ExecLog.IsSuccess)).NativeDbType(MySqlDbType.Bool);
            f.Member(t => t.Result).Field(nameof(ExecLog.Result)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.RetryTimes).Field(nameof(ExecLog.RetryTimes)).NativeDbType(MySqlDbType.Int32);
            f.Member(t => t.CreatedBy).Field(nameof(ExecLog.CreatedBy)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.CreatedAt).Field(nameof(ExecLog.CreatedAt)).NativeDbType(MySqlDbType.DateTime);
            f.Member(t => t.UpdatedBy).Field(nameof(ExecLog.UpdatedBy)).NativeDbType(MySqlDbType.VarChar);
            f.Member(t => t.UpdatedAt).Field(nameof(ExecLog.UpdatedAt)).NativeDbType(MySqlDbType.DateTime);
        });
    }
}
