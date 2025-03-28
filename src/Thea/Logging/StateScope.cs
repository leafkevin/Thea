using System;
using System.Threading;

namespace Thea.Logging;

public class StateScope : IDisposable
{
    private static readonly AsyncLocal<StateHolder> current = new();
    private static readonly StateScope instance = new();
    private StateScope() { }

    public static string TraceId => current.Value?.Value;
    public static StateScope Push(string traceId)
    {
        var holder = current.Value ??= new StateHolder();
        holder.Value = traceId;
        return instance;
    }
    public void Dispose() => current.Value = null;
    private sealed class StateHolder
    {
        public string Value;
    }
}
