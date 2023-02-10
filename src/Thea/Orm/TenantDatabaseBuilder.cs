﻿using System;

namespace Thea.Orm;

public class TenantDatabaseBuilder
{
    private readonly TheaDatabaseProvider databaseProvider;
    private readonly TheaDatabase database;

    public TenantDatabaseBuilder(TheaDatabaseProvider databaseProvider, TheaDatabase database)
    {
        this.databaseProvider = databaseProvider;
        this.database = database;
    }
    public TenantDatabaseBuilder With(params int[] tenantIds)
    {
        if (tenantIds != null)
            this.database.TenantIds = tenantIds;
        return this;
    }
    public TenantDatabaseBuilder Use(Type ormProviderType)
    {
        if (ormProviderType == null)
            throw new ArgumentNullException(nameof(ormProviderType));

        this.database.OrmProviderType = ormProviderType;
        return this;
    }
    public TenantDatabaseBuilder Configure(IModelConfiguration configuration)
    {
        if (!this.databaseProvider.TryGetEntityMapProvider(this.database.OrmProviderType, out var mapProvider))
            this.databaseProvider.AddEntityMapProvider(this.database.OrmProviderType, mapProvider = new EntityMapProvider());

        var modelBuilder = new ModelBuilder(mapProvider);
        configuration.OnModelCreating(modelBuilder);
        return this;
    }
}