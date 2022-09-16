using System;

namespace Thea
{
    public interface IOrmDbFactory
    {
        TheaDatabase Register(string dbKey, bool isDefault, Action<TheaDatabaseBuilder> databaseInitializer);
        void LoadFromConfigure(string sectionName);
        ModelBuilder Builder { get; }
        IRepository Create(TheaConnection connection);
        IRepository Create(string dbKey = null, int? tenantId = null);
        TheaDatabase GetDatabase(string dbKey = null);
        TheaConnString GetConnInfo(string dbKey, int? tenantId);
    }
}
