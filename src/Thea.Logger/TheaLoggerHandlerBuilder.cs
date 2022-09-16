using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Thea.Logger;

public class TheaLoggerHandlerBuilder : ILoggerHandlerBuilder
{
    private readonly IList<Func<LoggerHandlerDelegate, LoggerHandlerDelegate>> components = new List<Func<LoggerHandlerDelegate, LoggerHandlerDelegate>>();

    public TheaLoggerHandlerBuilder(IServiceProvider serviceProvider)
    {
        this.ServiceProvider = serviceProvider;
    }
    public IServiceProvider ServiceProvider { get; private set; }

    public ILoggerHandlerBuilder AddHandler(Func<LoggerHandlerDelegate, LoggerHandlerDelegate> middleware)
    {
        components.Add(middleware);
        return this;
    }
    public ILoggerHandlerBuilder AddHandler<TMiddleware>(params object[] args)
    {
        var type = typeof(TMiddleware);
        return this.AddHandler(next =>
        {
            var method = type.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public);
            var ctorArgs = new object[args.Length + 1];
            ctorArgs[0] = next;
            Array.Copy(args, 0, ctorArgs, 1, args.Length);
            var instance = TheaActivator.CreateInstance(this.ServiceProvider, type, ctorArgs);
            if (method.GetParameters().Length > 1)
            {
                throw new Exception("Invoke方法只允许有一个RequestHandlerContext类型参数！");
            }
            return (LoggerHandlerDelegate)method.CreateDelegate(typeof(LoggerHandlerDelegate), instance);
        });
    }
    public LoggerHandlerDelegate Build(LoggerHandlerDelegate first = null)
    {
        LoggerHandlerDelegate app = null;
        if (first == null) app = context => Task.FromResult(true);
        else app = context => first(context);
        foreach (var component in this.components.Reverse())
        {
            app = component(app);
        }
        return app;
    }
}
