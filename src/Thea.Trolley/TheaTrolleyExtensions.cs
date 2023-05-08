using Microsoft.Extensions.DependencyInjection;
using System;

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
    }
}