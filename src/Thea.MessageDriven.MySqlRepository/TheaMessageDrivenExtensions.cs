using Microsoft.Extensions.DependencyInjection;
using System;
using Trolley;

namespace Thea.MessageDriven.MySqlRepository;

public static class TheaMessageDrivenExtensions
{
    public static MessageDrivenBuilder UseTrolleyRepository(this MessageDrivenBuilder builder, string dbKey)
    {
        return builder.UseRepository(f =>
        {
            var dbFactory = f.GetRequiredService<IOrmDbFactory>();
            if (dbFactory == null) throw new Exception("请先注册Trolley Orm组件");
            dbFactory.Configure(dbKey, new ModelConfiguration());
            dbFactory.Build();
            return new DefaultRepository(f);
        });
    }
}
