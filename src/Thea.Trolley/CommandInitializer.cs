using System.Data;

namespace Thea.Trolley;

public delegate void CommandInitializer(IDbCommand command, params object[] parameters);
public delegate void PagedCommandInitializer(IDbCommand command, int pageIndex, int pageSize, string orderBy, params object[] parameters);
