﻿using System.Collections.Generic;

namespace Thea.Orm;

public interface IPagedList<T>
{
    int RecordsTotal { get; }
    List<T> Items { get; }
}
