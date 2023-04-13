using System.Collections.Generic;
using System.Linq.Expressions;

namespace Thea.Orm;

public interface ISqlVisitor
{
    SqlSegment VisitAndDeferred(SqlSegment sqlSegment);
    SqlSegment Visit(SqlSegment sqlSegment);

    T Evaluate<T>(Expression expr);
    SqlSegment Evaluate(SqlSegment sqlSegment);

    List<SqlSegment> ConvertFormatToConcatList(SqlSegment[] argsSegments);
    List<SqlSegment> SplitConcatList(SqlSegment[] argsSegments);
}
