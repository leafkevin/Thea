using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Thea.MessageDriven;

public static class TheaMessageDrivenExtensions
{
    public static IServiceCollection AddMessageDriven(this IServiceCollection services)
    {
        services.AddSingleton<MessageDrivenService>();
        services.AddSingleton<IMessageDriven>(f => f.GetRequiredService<MessageDrivenService>());
        services.AddHostedService(f => f.GetRequiredService<MessageDrivenService>());
        return services;
    }
    public static IApplicationBuilder UseMessageDriven(this IApplicationBuilder app, Action<MessageDrivenBuilder> builderInitializer)
    {
        var builder = new MessageDrivenBuilder(app.ApplicationServices);
        builderInitializer.Invoke(builder);
        builder.Build().Start();
        return app;
    }
    public static MessageDrivenBuilder UseStatefulConsumer<TConsumer>(this MessageDrivenBuilder buidler, string exchange, string queue, Expression<Func<TConsumer, Delegate>> consumerHandlerSelector, bool isSingleActiveConsumer = true, bool isQuorumQueue = true)
    {
        var consumerHandler = VisitExpr(consumerHandlerSelector.Body);
        buidler.UseStatefulConsumer<TConsumer>(exchange, queue, consumerHandler, isSingleActiveConsumer, isQuorumQueue);
        return buidler;
    }
    public static MessageDrivenBuilder UseSubscriber<TConsumer>(this MessageDrivenBuilder buidler, string queue, Expression<Func<TConsumer, Delegate>> consumerHandlerSelector, bool isQuorumQueue = true)
    {
        var consumerHandler = VisitExpr(consumerHandlerSelector.Body);
        buidler.UseSubscriber<TConsumer>(queue, consumerHandler, isQuorumQueue);
        return buidler;
    }
    public static MessageDrivenBuilder UseSubscriber<TConsumer>(this MessageDrivenBuilder buidler, string exchange, string queue, Expression<Func<TConsumer, Delegate>> consumerHandlerSelector, bool isDelay = false, bool isQuorumQueue = true)
    {
        var consumerHandler = VisitExpr(consumerHandlerSelector.Body);
        buidler.UseSubscriber<TConsumer>(exchange, queue, consumerHandler, isDelay, isQuorumQueue);
        return buidler;
    }
    private static MethodInfo VisitExpr(Expression expr)
    {
        MethodInfo result = null;
        var myExpr = expr;
        while (true)
        {
            if (myExpr is UnaryExpression unaryExpr)
            {
                myExpr = unaryExpr.Operand;
                continue;
            }
            else if (myExpr is MethodCallExpression callExpr && callExpr.Method.Name.StartsWith("CreateDelegate"))
            {
                myExpr = callExpr.Object;
                continue;
            }
            else if (myExpr is ConstantExpression constantExpr)
            {
                result = constantExpr.Value as MethodInfo;
                break;
            }
            else if (myExpr is MemberExpression memberExpr)
            {
                result = memberExpr.Member as MethodInfo;
                break;
            }
            else throw new NotSupportedException("不支持的表达式，只支持类方法的成员访问");
        }
        return result;
    }
}