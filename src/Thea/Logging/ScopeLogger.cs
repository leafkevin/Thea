using System.Threading;

namespace Thea.Logging;

public class ScopeLogger
{
    private static readonly AsyncLocal<LogEntity> logger = new();
    public static bool TryGetScope(out LogEntity state)
    {
        state = logger.Value;
        return state != null;
    }
    public static void Update(LogEntity scopeState)
    {
        if (logger.Value == null)
            logger.Value = scopeState;
        else logger.Value.LoadFrom(scopeState);
    }
    public static LogEntity Release()
    {
        var lastState = logger.Value;
        logger.Value = null;
        return lastState;
    }
}