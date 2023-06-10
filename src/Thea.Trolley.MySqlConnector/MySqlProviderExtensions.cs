using MySqlConnector;
using Thea.Orm;

namespace Thea.Trolley.MySqlConnector;

public static class MySqlProviderExtensions
{
    public static MemberBuilder<TMember> NativeDbType<TMember>(this MemberBuilder<TMember> builder, MySqlDbType nativeDbType)
        => builder.NativeDbType(nativeDbType);
}
