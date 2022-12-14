using System;
using Thea.Orm;

namespace Thea.Trolley;

public class OrmDbFactoryBuilder
{
    private readonly IOrmDbFactory dbFactory;
    public OrmDbFactoryBuilder() => this.dbFactory = new OrmDbFactory();
    public OrmDbFactoryBuilder Register(string dbKey, bool isDefault, Action<TheaDatabaseBuilder> databaseInitializer)
    {
        this.dbFactory.Register(dbKey, isDefault, databaseInitializer);
        return this;
    }
    public OrmDbFactoryBuilder Configure(IModelConfiguration configuration)
    {
        this.dbFactory.Configure(f => configuration.OnModelCreating(f));
        return this;
    }
    public OrmDbFactoryBuilder Configure<TModelConfiguration>() where TModelConfiguration : class, IModelConfiguration, new()
    {
        this.dbFactory.Configure(f => new TModelConfiguration().OnModelCreating(f));
        return this;
    }
    public OrmDbFactoryBuilder Configure(Action<ModelBuilder> initializer)
    {
        this.dbFactory.Configure(initializer);
        return this;
    }
    public IOrmDbFactory Build() => this.dbFactory;
}
