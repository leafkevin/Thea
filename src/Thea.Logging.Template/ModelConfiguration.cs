using Thea.Logging.Template;
using Thea.Orm;

namespace Thea.Logging;

class ModelConfiguration : IModelConfiguration
{
    public void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<LogTemplate>(f =>
        {
            f.ToTable("sys_log_template").Key(t => t.Id);
            f.Member(t => t.Id).Field(nameof(LogTemplate.Id)).NativeDbType(253);
            f.Member(t => t.TenantId).Field(nameof(LogTemplate.TenantId)).NativeDbType(3);
            f.Member(t => t.Category).Field(nameof(LogTemplate.Category)).NativeDbType(253);
            f.Member(t => t.ApiUrl).Field(nameof(LogTemplate.ApiUrl)).NativeDbType(253);
            f.Member(t => t.TagFrom).Field(nameof(LogTemplate.TagFrom)).NativeDbType(253);
            f.Member(t => t.TagRegex).Field(nameof(LogTemplate.TagRegex)).NativeDbType(253);
            f.Member(t => t.Template).Field(nameof(LogTemplate.Template)).NativeDbType(253);
            f.Member(t => t.ReviseTime).Field(nameof(LogTemplate.ReviseTime)).NativeDbType(12);
        });
        builder.Entity<OperationLog>(f =>
        {
            f.ToTable("sys_operation_log").Key(t => t.Id);
            f.Member(t => t.Id).Field(nameof(OperationLog.Id)).NativeDbType(253);
            f.Member(t => t.TenantId).Field(nameof(OperationLog.TenantId)).NativeDbType(3);
            f.Member(t => t.Category).Field(nameof(OperationLog.Category)).NativeDbType(253);
            f.Member(t => t.UserId).Field(nameof(OperationLog.UserId)).NativeDbType(253);
            f.Member(t => t.ApiUrl).Field(nameof(OperationLog.ApiUrl)).NativeDbType(253);
            f.Member(t => t.Tag).Field(nameof(OperationLog.Tag)).NativeDbType(253);
            f.Member(t => t.Body).Field(nameof(OperationLog.Body)).NativeDbType(253);
            f.Member(t => t.ClientIp).Field(nameof(OperationLog.ClientIp)).NativeDbType(253);
            f.Member(t => t.CreatedAt).Field(nameof(OperationLog.CreatedAt)).NativeDbType(12);
        });
    }
}