using System;
using System.Collections.Generic;

namespace Thea
{
    public class TheaDatabase
    {
        public string DbKey { get; set; }
        public bool IsDefault { get; set; }
        public List<TheaConnString> ConnectionStrings { get; set; }
        public TheaDatabase() { }
        public TheaDatabase(string dbKey, bool isDefault = false)
        {
            this.DbKey = dbKey;
            this.IsDefault = isDefault;
        }
        public TheaConnString GetConnString(int? tenantId)
        {
            TheaConnString result = null;
            if (tenantId.HasValue)
            {
                result = this.ConnectionStrings.Find(f => f.TenantIds != null && f.TenantIds.Contains(tenantId.Value));
                if (result != null) return result;
            }

            result = this.ConnectionStrings.Find(f => f.IsDefault);
            if (result == null)
                throw new Exception($"dbKey:{this.DbKey}数据库未配置默认连接串");

            return result;
        }
    }
    public class TheaConnString
    {
        public string DbKey { get; set; }
        public string ConnectionString { get; set; }
        public IOrmProvider OrmProvider { get; set; }
        public bool IsDefault { get; set; }
        public List<int> TenantIds { get; set; }

        public TheaConnString() { }
        public TheaConnString(string dbKey, string connectionString, IOrmProvider ormProvider, bool isDefault, List<int> tenantIds = null)
        {
            this.DbKey = dbKey;
            this.ConnectionString = connectionString;
            this.OrmProvider = ormProvider;
            this.IsDefault = isDefault;
            this.TenantIds = tenantIds;
        }
    }
}
