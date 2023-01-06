using Microsoft.Extensions.DependencyInjection;
using System;
using Thea.Orm;

namespace Thea.Trolley
{
    public static class TheaExtensions
    {
        public static IServiceCollection AddTrolley(this IServiceCollection services, Action<OrmDbFactoryBuilder> initializer)
        {
            services.AddSingleton<IOrmDbFactory, OrmDbFactory>(f =>
            {
                var dbFactory = new OrmDbFactory(f);
                var builder = new OrmDbFactoryBuilder(dbFactory);
                initializer?.Invoke(builder);
                return dbFactory;
            });
            return services;
        }
    }
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
            var builder = new ModelBuilder(this.dbFactory);
            configuration.OnModelCreating(builder);
            return this;
        }
        public OrmDbFactoryBuilder Configure<TModelConfiguration>() where TModelConfiguration : class, IModelConfiguration, new()
        {
            var builder = new ModelBuilder(this.dbFactory);
            var configuration = new TModelConfiguration();
            configuration.OnModelCreating(builder);
            return this;
        }
        public OrmDbFactoryBuilder Configure(Action<ModelBuilder> initializer)
        {
            var builder = new ModelBuilder(this.dbFactory);
            initializer.Invoke(builder);
            return this;
        }
        public OrmDbFactoryBuilder LoadFromConfigure(string sectionName)
        {
            this.dbFactory.LoadFromConfiguration(sectionName);
            return this;
        }
    }
}
