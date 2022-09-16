using System;
using Thea.Orm;

namespace Thea.Trolley;

public class OrmDbFactoryBuilder
{
    private readonly IOrmDbFactory dbFactory;
    internal OrmDbFactoryBuilder(IOrmDbFactory dbFactory) => this.dbFactory = dbFactory;
    public OrmDbFactoryBuilder Register(string dbKey, bool isDefault, Action<TheaDatabaseBuilder> databaseInitializer)
    {
        this.dbFactory.Register(dbKey, isDefault, databaseInitializer);
        return this;
    }
    public OrmDbFactoryBuilder Configure(IModelConfiguration configuration)
    {
        var builder = this.dbFactory.CreateModelBuidler();
        configuration.OnModelCreating(builder);
        return this;
    }
    public OrmDbFactoryBuilder Configure<TModelConfiguration>() where TModelConfiguration : class, IModelConfiguration, new()
    {
        var builder = this.dbFactory.CreateModelBuidler();
        var configuration = new TModelConfiguration();
        configuration.OnModelCreating(builder);
        return this;
    }
    public OrmDbFactoryBuilder LoadFromConfigure(string sectionName)
    {
        this.dbFactory.LoadFromConfigure(sectionName);
        return this;
    }
}
