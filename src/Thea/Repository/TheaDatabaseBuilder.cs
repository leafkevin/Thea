using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Thea
{
    public class TheaDatabaseBuilder
    {
        private readonly ConcurrentDictionary<Type, IOrmProvider> ormProviders;
        private readonly TheaDatabase database;

        public TheaDatabaseBuilder(ConcurrentDictionary<Type, IOrmProvider> ormProviders, TheaDatabase database)
        {
            this.ormProviders = ormProviders;
            this.database = database;
        }
        public TheaDatabaseBuilder Add<TOrmProvider>(string connString, bool isDefault, List<int> tenantIds = null) where TOrmProvider : class, IOrmProvider, new()
        {
            var type = typeof(TOrmProvider);
            if (!ormProviders.TryGetValue(type, out var ormProvider))
            {
                ormProviders.TryAdd(type, ormProvider = new TOrmProvider());
            }
            this.database.ConnectionStrings.Add(new TheaConnString(this.database.DbKey, connString, ormProvider, isDefault, tenantIds));
            return this;
        }
    }
}
