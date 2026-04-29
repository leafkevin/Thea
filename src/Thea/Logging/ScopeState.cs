using System.Collections.Generic;
using System.Threading;

namespace Thea.Logging;

public class ScopeState
{
    private static readonly AsyncLocal<Stack<LogEntity>> states = new();
    public static bool TryGetState(out LogEntity state)
    {
        if (states.Value == null || states.Value.Count == 0)
        {
            state = null;
            return false;
        }
        state = states.Value.Peek();
        return true;
    }
    public static void Push(LogEntity scopeState)
    {
        states.Value ??= new();
        if (states.Value.TryPop(out var lastScopeState))
            scopeState = lastScopeState.DecorateFrom(scopeState);
        states.Value.Push(scopeState);
    }
    public static LogEntity Pop()
    {
        if (states.Value != null && states.Value.Count > 0)
        {
            var scopeState = states.Value.Pop();
            if (states.Value.Count == 0)
                states.Value = null;
            return scopeState;
        }
        return null;
    }
}