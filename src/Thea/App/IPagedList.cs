using System.Collections.Generic;

namespace Thea;

public interface IPagedList<T>
{
    int RecordsTotal { get; }
    List<T> Items { get; }
}
