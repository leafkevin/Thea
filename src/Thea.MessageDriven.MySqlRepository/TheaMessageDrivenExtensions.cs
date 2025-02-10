using System;
using Microsoft.Extensions.DependencyInjection;
using Trolley;

namespace Thea.MessageDriven.MySqlRepository;

public static class TheaMessageDrivenExtensions
{
    public static MessageDrivenBuilder UseTrolleyRepository(this MessageDrivenBuilder builder, string dbKey)
    {
        builder.UseRepository(new DefaultRepository(builder.ServiceProvider));
        var dbFactory = builder.ServiceProvider.GetService<IOrmDbFactory>();
        if (dbFactory == null) throw new Exception("请先注册Trolley Orm组件");
        dbFactory.Configure(dbKey, new ModelConfiguration());
        dbFactory.Build();
        return builder;
    }
}
