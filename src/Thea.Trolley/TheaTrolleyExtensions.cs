﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Thea.Orm;

namespace Thea.Trolley
{
    public static class TheaTrolleyExtensions
    {
        public static IServiceCollection AddTrolley(this IServiceCollection services, Action<OrmDbFactoryBuilder> initializer)
        {
            var builder = new OrmDbFactoryBuilder();
            initializer.Invoke(builder);
            services.AddSingleton(builder.Build());
            return services;
        }
        public static OrmDbFactoryBuilder LoadFromConfiguration(this OrmDbFactoryBuilder builder, IConfiguration configuration, string sectionName)
        {
            var databases = configuration.GetSection(sectionName).GetChildren();
            foreach (var configInfo in databases)
            {
                var databaseProvider = new TheaDatabaseProvider { DbKey = configInfo.Key };
                configInfo.Bind(databaseProvider);
                var connStrings = configInfo.GetSection("ConnectionStrings").GetChildren();
                foreach (var connString in connStrings)
                {
                    var database = new TheaDatabase { DbKey = configInfo.Key };
                    connString.Bind(database);

                    var ormProviderTypeName = connString.GetValue<string>("OrmProvider");
                    var ormProviderType = typeof(OrmDbFactory).Assembly.GetType(ormProviderTypeName);
                    builder.Register(database.DbKey, database.IsDefault, f =>
                        f.Add(database).Use(ormProviderType));
                }
            }
            return builder;
        }
        public static OrmDbFactoryBuilder AddTypeHandler<TTypeHandler>(this OrmDbFactoryBuilder builder) where TTypeHandler : class, ITypeHandler, new()
            => builder.AddTypeHandler(new TTypeHandler());
        public static OrmDbFactoryBuilder Configure<TOrmProvider, TModelConfiguration>(this OrmDbFactoryBuilder builder) where TModelConfiguration : class, IModelConfiguration, new()
        {
            builder.Configure(typeof(TOrmProvider), new TModelConfiguration());
            return builder;
        }
    }
}