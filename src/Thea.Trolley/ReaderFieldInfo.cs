using System.Reflection;
using Thea.Orm;

namespace Thea.Trolley;

public class ReaderFieldInfo
{
    public int Index { get; set; }
    public bool IsTarget { get; set; } = true;
    public MemberInfo Member { get; set; }
    public EntityMap RefMapper { get; set; }
    public string MemberName { get; set; }
}
