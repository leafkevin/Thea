using System;

namespace Thea.Orm;

public interface IOrmDbFactory
{
    TheaDatabase Register(string dbKey, bool isDefault, Action<TheaDatabaseBuilder> databaseInitializer);
    void LoadFromConfiguration(string sectionName);
    IRepository Create(TheaConnection connection);
    IRepository Create(string dbKey = null, int? tenantId = null);
    TheaConnectionInfo GetConnectionInfo(string dbKey = null, int? tenantId = null);
    TheaDatabase GetDatabase(string dbKey = null);
    void Configure(Action<ModelBuilder> modelInitializer);
    void AddEntityMap(Type entityType, EntityMap mapper);
    bool TryGetEntityMap(Type entityType, out EntityMap mapper);
}
