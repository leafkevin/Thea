using System.Collections.Generic;
using System.Threading;
using Thea.Logging;

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
        states.Value.Push(scopeState);
    }
    public static void Pop()
    {
        if (states.Value != null && states.Value.Count > 0)
            states.Value.Pop();
    }
}