using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;

namespace Thea.Orm;

public interface IDeleteVisitor
{
    string BuildSql(out List<IDbDataParameter> dbParameters);
    IDeleteVisitor Where(Expression whereExpr);
    IDeleteVisitor And(Expression whereExpr);
}