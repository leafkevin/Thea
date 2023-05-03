using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;

namespace Thea.Orm;

public interface ICreateVisitor
{
    string BuildSql(out List<IDbDataParameter> dbParameters);
    ICreateVisitor From(Expression fieldSelector);
    ICreateVisitor Where(Expression whereExpr);
    ICreateVisitor And(Expression whereExpr); 
}
