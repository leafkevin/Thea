using System;
using System.Threading;

namespace Thea.Logging;

public class TheaLogScope : IDisposable
{
    private static readonly AsyncLocal<TheaLogScope> current = new AsyncLocal<TheaLogScope>();
    private int disposed;
    public TheaLogState State { get; private set; }
    public TheaLogScope Parent { get; private set; }
    public static TheaLogScope Current => current.Value;

    public static TheaLogScope Push(TheaLogState state)
    {
        TheaLogScope newScope = null;
        var parentScope = current.Value;
        if (parentScope != null)
        {
            if (!string.IsNullOrEmpty(state.TraceId))
            {
                var ancestor = parentScope;
                while (ancestor != null && ancestor.State != null)
                {
                    if (!string.IsNullOrEmpty(ancestor.State.TraceId))
                        break;
                    ancestor.State.TraceId = state.TraceId;
                    ancestor = ancestor.Parent;
                }
            }
            else state.TraceId = parentScope.State.TraceId;

            if (!string.IsNullOrEmpty(state.Tag))
            {
                var ancestor = parentScope;
                while (ancestor != null && ancestor.State != null)
                {
                    if (!string.IsNullOrEmpty(ancestor.State.Tag))
                        break;
                    ancestor.State.Tag = state.Tag;
                    ancestor = ancestor.Parent;
                }
            }
            else state.Tag = parentScope.State.Tag;

            if (state.Tag != parentScope.State.Tag)
            {
                state.Sequence = parentScope.State.Sequence + 1;
                newScope = new TheaLogScope();
                newScope.State = state;
                newScope.Parent = parentScope;
                current.Value = newScope;
            }
            else newScope = parentScope;
        }
        else
        {
            if (string.IsNullOrEmpty(state.TraceId))
                throw new ArgumentNullException("TraceId");

            state.Sequence = 1;
            newScope = new TheaLogScope { State = state };
            current.Value = newScope;
        }
        return newScope;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 1)
        {
            if (current.Value != null)
                current.Value = current.Value.Parent;
        }
    }
}
