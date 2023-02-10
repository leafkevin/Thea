using System;

namespace Thea.Orm;

public interface ITypeHandlerProvider
{
    void AddTypeHandler(ITypeHandler typeHandler);
    bool TryGetTypeHandler(Type handlerType, out ITypeHandler typeHandler);
}
