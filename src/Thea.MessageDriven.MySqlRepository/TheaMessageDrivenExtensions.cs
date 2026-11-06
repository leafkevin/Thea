using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Trolley;

namespace Thea.MessageDriven.MySqlRepository;

public static class TheaMessageDrivenExtensions
{
    public static MessageDrivenBuilder UseTrolleyRepository(this MessageDrivenBuilder builder)
    {
        return builder.UseRepository(f =>
        {
            var configuration = f.GetRequiredService<IConfiguration>();
            var dbKey = configuration.GetValue<string>("MessageDriven:DbKey");
            var dbFactory = f.GetRequiredService<IOrmDbFactory>();
            if (dbFactory == null) throw new Exception("请先注册Trolley Orm组件");
            dbFactory.UseMapping<ModelConfiguration>(dbKey);
            dbFactory.Build();
            return new DefaultRepository(f);
        });
    }
}