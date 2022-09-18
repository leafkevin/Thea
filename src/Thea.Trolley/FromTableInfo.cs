using System;
using Thea.Orm;

namespace Thea.Trolley;

public class FromTableInfo
{
    public Type EntityType { get; set; }
    public EntityMap Mapper { get; set; }
    public string AlaisName { get; set; }
    public string JoinType { get; set; }
    public string JoinOn { get; set; }
    public bool IsInclude { get; set; }
}