using System;
using System.Threading;

namespace Thea.Logging;

public class StateScope : IDisposable
{
    private static readonly AsyncLocal<StateHolder> current = new();
    private static readonly StateScope instance = new();
    private StateScope() { }
    public static LogEntity State => current.Value?.State;
    public static StateScope Push(object scopeState)
    {
        var holder = current.Value ??= new StateHolder();
        if (scopeState is LogEntity logEntity)
            holder.State = logEntity; 
        return instance;
    }
    public void Dispose() => current.Value = null;
    private sealed class StateHolder
    {
        public LogEntity State;
    }
}