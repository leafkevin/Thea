using System.Collections.Generic;

namespace Thea.Orm;

public class TheaConnectionInfo
{
    public string DbKey { get; set; }
    public string ConnectionString { get; set; }
    public IOrmProvider OrmProvider { get; set; }
    public bool IsDefault { get; set; }
    public List<int> TenantIds { get; set; }
}

