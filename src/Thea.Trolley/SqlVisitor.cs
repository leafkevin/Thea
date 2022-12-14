using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Text;
using Thea.Orm;

namespace Thea.Trolley;

public class SqlVisitor
{
    private delegate string ToSqlDelegate(object query, out List<IDbDataParameter> dbParameters);
    private static ConcurrentDictionary<Type, ToSqlDelegate> toSqlCache = new();
    protected List<TableSegment> tables;
    protected Dictionary<string, TableSegment> tableAlias;
    protected List<IDbDataParameter> dbParameters;
    protected readonly string parameterPrefix;
    protected bool isNeedAlias = false;
    internal char tableStartAs = 'a';
    internal readonly IOrmDbFactory dbFactory;
    internal readonly TheaConnection connection;
    internal readonly IDbTransaction transaction;
    internal readonly IOrmProvider ormProvider;

    public SqlVisitor(IOrmDbFactory dbFactory, TheaConnection connection, IDbTransaction transaction, char tableStartAs = 'a', string parameterPrefix = "p")
    {
        this.dbFactory = dbFactory;
        this.connection = connection;
        this.transaction = transaction;
        this.ormProvider = connection.OrmProvider;
        this.tableStartAs = tableStartAs;
        this.parameterPrefix = parameterPrefix;
    }
    public virtual SqlSegment VisitAndDeferred(SqlSegment sqlSegment)
        => this.VisitBooleanDeferred(this.Visit(sqlSegment));
    public virtual SqlSegment Visit(SqlSegment sqlSegment)
    {
        if (sqlSegment == SqlSegment.None)
            return SqlSegment.None;

        SqlSegment result = null;
        while (sqlSegment.Expression != null)
        {
            switch (sqlSegment.Expression.NodeType)
            {
                case ExpressionType.Lambda:
                    var lambdaExpr = sqlSegment.Expression as LambdaExpression;
                    if (this.IsFromQueryLambda(lambdaExpr, out var elementType))
                        return this.VisitFromQuery(sqlSegment, elementType);
                    sqlSegment.Expression = lambdaExpr.Body;
                    continue;
                case ExpressionType.Negate:
                case ExpressionType.NegateChecked:
                case ExpressionType.Not:
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                case ExpressionType.ArrayLength:
                case ExpressionType.Quote:
                case ExpressionType.TypeAs:
                    result = this.VisitUnary(sqlSegment);
                    break;
                case ExpressionType.MemberAccess:
                    result = this.VisitMemberAccess(sqlSegment);
                    break;
                case ExpressionType.Constant:
                    result = this.VisitConstant(sqlSegment);
                    break;
                case ExpressionType.Add:
                case ExpressionType.AddChecked:
                case ExpressionType.Subtract:
                case ExpressionType.SubtractChecked:
                case ExpressionType.Multiply:
                case ExpressionType.MultiplyChecked:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.And:
                case ExpressionType.AndAlso:
                case ExpressionType.Or:
                case ExpressionType.OrElse:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.Coalesce:
                case ExpressionType.ArrayIndex:
                case ExpressionType.RightShift:
                case ExpressionType.LeftShift:
                case ExpressionType.ExclusiveOr:
                    result = this.VisitBinary(sqlSegment);
                    break;
                case ExpressionType.Parameter:
                    result = this.VisitParameter(sqlSegment);
                    break;
                case ExpressionType.Call:
                    result = this.VisitMethodCall(sqlSegment);
                    break;
                case ExpressionType.New:
                    result = this.VisitNew(sqlSegment);
                    break;
                case ExpressionType.NewArrayInit:
                case ExpressionType.NewArrayBounds:
                    result = this.VisitNewArray(sqlSegment);
                    break;
                case ExpressionType.MemberInit:
                    result = this.VisitMemberInit(sqlSegment);
                    break;
                case ExpressionType.Index:
                    result = this.VisitIndexExpression(sqlSegment);
                    break;
                case ExpressionType.Conditional:
                    result = this.VisitConditional(sqlSegment);
                    break;
            }
            return result;
        }
        return result;
    }
    public virtual SqlSegment VisitUnary(SqlSegment sqlSegment)
    {
        var unaryExpr = sqlSegment.Expression as UnaryExpression;
        switch (unaryExpr.NodeType)
        {
            case ExpressionType.Not:
                if (unaryExpr.Type == typeof(bool))
                {
                    //目前只有Not，一元/二元 bool类型才有延时处理,到参数表达式再统一处理
                    sqlSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                    return this.Visit(sqlSegment.Next(unaryExpr.Operand));
                    //if (unaryExpr.Operand.IsParameter(out _))
                    //{
                    //    //目前只有Not，一元/二元 bool类型才有延时处理,到参数表达式再统一处理
                    //    sqlSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                    //    return this.Visit(sqlSegment.Next(unaryExpr.Operand));
                    //}
                    ////TODO:测试Value赋值
                    //return sqlSegment.Change($"NOT ({this.VisitAndDeferred(sqlSegment.Next(unaryExpr.Operand))})");
                }
                return sqlSegment.Change($"~{this.Visit(sqlSegment)}");
            case ExpressionType.Convert:
                if (unaryExpr.Method != null)
                {
                    //TODO:测试类型转换
                    if (unaryExpr.Operand.IsParameter(out _))
                        return this.Visit(sqlSegment.Next(unaryExpr.Operand));
                    return this.EvaluateAndParameter(sqlSegment);
                }
                break;
        }
        return this.Visit(sqlSegment.Next(unaryExpr.Operand));
    }
    public virtual SqlSegment VisitBinary(SqlSegment sqlSegment)
    {
        var binaryExpr = sqlSegment.Expression as BinaryExpression;
        switch (binaryExpr.NodeType)
        {
            //And/Or，已经在Where/Having中单独处理了
            case ExpressionType.Add:
            case ExpressionType.AddChecked:
            case ExpressionType.Subtract:
            case ExpressionType.SubtractChecked:
            case ExpressionType.Multiply:
            case ExpressionType.MultiplyChecked:
            case ExpressionType.Divide:
            case ExpressionType.Modulo:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.Coalesce:
            case ExpressionType.ArrayIndex:
            case ExpressionType.And:
            case ExpressionType.Or:
            case ExpressionType.ExclusiveOr:
            case ExpressionType.RightShift:
            case ExpressionType.LeftShift:
                //表达式计算
                switch (binaryExpr.NodeType)
                {
                    case ExpressionType.Add:
                    case ExpressionType.AddChecked:
                    case ExpressionType.Subtract:
                    case ExpressionType.SubtractChecked:
                    case ExpressionType.Multiply:
                    case ExpressionType.MultiplyChecked:
                    case ExpressionType.Divide:
                    case ExpressionType.Modulo:
                    case ExpressionType.Coalesce:
                    case ExpressionType.ArrayIndex:
                    case ExpressionType.And:
                    case ExpressionType.Or:
                    case ExpressionType.ExclusiveOr:
                    case ExpressionType.RightShift:
                    case ExpressionType.LeftShift:
                        sqlSegment.IsExpression = true;
                        sqlSegment.IsConstantValue = false;
                        break;
                }
                //字符串连接单独处理
                if (binaryExpr.NodeType == ExpressionType.Add && (binaryExpr.Left.Type == typeof(string) || binaryExpr.Right.Type == typeof(string)))
                    return this.VisitConcatAndDeferred(sqlSegment);

                var leftSegment = this.Visit(sqlSegment.Next(binaryExpr.Left));
                var rightSegment = this.Visit(new SqlSegment { Expression = binaryExpr.Right });
                var operators = this.GetOperator(binaryExpr.NodeType);

                if (binaryExpr.NodeType == ExpressionType.Modulo || binaryExpr.NodeType == ExpressionType.Coalesce)
                    return leftSegment.Change($"{operators}({leftSegment},{rightSegment})", false);

                if (binaryExpr.NodeType == ExpressionType.Equal || binaryExpr.NodeType == ExpressionType.NotEqual)
                {
                    if (!leftSegment.HasField && rightSegment.HasField)
                        this.Swap(ref leftSegment, ref rightSegment);

                    //处理!(a.IsEnabled==true)情况,bool类型，最外层再做defer处理
                    if (binaryExpr.Left.Type == typeof(bool) && leftSegment.HasField && !rightSegment.HasField)
                    {
                        if (!(bool)rightSegment.Value)
                            leftSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                        if (binaryExpr.NodeType == ExpressionType.NotEqual)
                            leftSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                        return sqlSegment.Merge(leftSegment);
                    }
                    //处理a.UserName!=null情况
                    if (leftSegment == SqlSegment.Null)
                        this.Swap(ref leftSegment, ref rightSegment);
                    if (rightSegment == SqlSegment.Null)
                    {
                        leftSegment.Push(new DeferredExpr
                        {
                            OperationType = OperationType.Equal,
                            Value = SqlSegment.Null
                        });
                        if (binaryExpr.NodeType == ExpressionType.NotEqual)
                            leftSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                        return leftSegment;
                    }
                }
                return leftSegment.Change($"{this.GetSqlValue(leftSegment)}{operators}{this.GetSqlValue(rightSegment)}", sqlSegment.IsConstantValue);
        }
        return sqlSegment;
    }
    public virtual SqlSegment VisitMemberAccess(SqlSegment sqlSegment)
    {
        var memberExpr = sqlSegment.Expression as MemberExpression;
        MemberAccessSqlFormatter formatter = null;
        if (memberExpr.Expression != null)
        {
            //Where(f=>... && !f.OrderId.HasValue && ...)
            //Where(f=>... f.OrderId.Value==10 && ...)
            //Select(f=>... ,f.OrderId.HasValue  ...)
            //Select(f=>... ,f.OrderId.Value==10  ...)
            if (Nullable.GetUnderlyingType(memberExpr.Member.DeclaringType) != null)
            {
                if (memberExpr.Member.Name == nameof(Nullable<bool>.HasValue))
                {
                    sqlSegment.Push(new DeferredExpr { OperationType = OperationType.Equal, Value = SqlSegment.Null });
                    sqlSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                    return sqlSegment.Next(memberExpr.Expression);
                }
                else if (memberExpr.Member.Name == nameof(Nullable<bool>.Value))
                    return sqlSegment.Next(memberExpr.Expression);
                else throw new ArgumentException($"不支持的MemberAccess操作，表达式'{memberExpr}'返回值不是boolean类型");
            }

            //各种类型值的属性访问，如：DateTime,TimeSpan,String.Length,List.Count
            if (this.ormProvider.TryGetMemberAccessSqlFormatter(memberExpr.Member, out formatter))
            {
                //Where(f=>... && f.CreatedAt.Month<5 && ...)
                //Where(f=>... && f.Order.OrderNo.Length==10 && ...)
                var targetSegment = this.Visit(sqlSegment.Next(memberExpr.Expression));
                return sqlSegment.Change(formatter.Invoke(targetSegment), false);
            }

            if (memberExpr.IsParameter(out var parameterName))
            {
                //Where(f=>... && f.Amount>5 && ...)
                //Include(f=>f.Buyer); 或是 IncludeMany(f=>f.Orders)
                //Select(f=>new {f.OrderId, ...})
                //Where(f=>f.Order.Id>10)
                //Include(f=>f.Order.Buyer)
                //Select(f=>new {f.Order.OrderId, ...})
                //GroupBy(f=>new {f.Order.OrderId, ...})
                //GroupBy(f=>f.Order.OrderId)
                //OrderBy(f=>new {f.Order.OrderId, ...})
                //OrderBy(f=>f.Order.OrderId)
                string path = null;
                TableSegment tableSegment = null;

                if (memberExpr.Type.IsEntityType())
                {
                    path = memberExpr.Expression.ToString();
                    var fromSegment = this.FindTableSegment(parameterName, path);
                    if (fromSegment == null)
                        throw new Exception($"使用导航属性前，要先使用Include方法包含进来，访问路径:{path}");
                    fromSegment.Mapper ??= this.dbFactory.GetEntityMap(fromSegment.EntityType);

                    var vavigationMapper = fromSegment.Mapper.GetMemberMap(memberExpr.Member.Name);
                    if (!vavigationMapper.IsNavigation)
                        throw new Exception($"类{tableSegment.EntityType.FullName}属性{memberExpr.Member.Name}未配置为导航属性");

                    path = memberExpr.ToString();
                    tableSegment = this.FindTableSegment(parameterName, path);
                    if (tableSegment == null)
                        throw new Exception($"使用导航属性前，要先使用Include方法包含进来，访问路径:{path}");

                    var readerFields = this.AddTableReaderFields(sqlSegment.ReaderIndex, tableSegment);
                    return new SqlSegment
                    {
                        HasField = true,
                        IsConstantValue = false,
                        TableSegment = tableSegment,
                        Value = readerFields
                    };
                }
                else
                {
                    path = memberExpr.Expression.ToString();
                    tableSegment = this.FindTableSegment(parameterName, path);
                    if (tableSegment == null)
                        throw new Exception($"使用导航属性前，要先使用Include方法包含进来，访问路径:{path}");

                    tableSegment.Mapper ??= this.dbFactory.GetEntityMap(tableSegment.EntityType);
                    var memberMapper = tableSegment.Mapper.GetMemberMap(memberExpr.Member.Name);
                    var fieldName = this.ormProvider.GetFieldName(memberMapper.FieldName);
                    if (this.isNeedAlias)
                        fieldName = tableSegment.AliasName + "." + fieldName;

                    if (sqlSegment.HasDeferred)
                    {
                        sqlSegment.HasField = true;
                        sqlSegment.IsConstantValue = false;
                        sqlSegment.TableSegment = tableSegment;
                        sqlSegment.FromMember = memberMapper.Member;
                        sqlSegment.Value = fieldName;
                        return this.VisitBooleanDeferred(sqlSegment);
                    }
                    sqlSegment.HasField = true;
                    sqlSegment.IsConstantValue = false;
                    sqlSegment.TableSegment = tableSegment;
                    sqlSegment.FromMember = memberMapper.Member;
                    sqlSegment.Value = fieldName;
                    return sqlSegment;
                }
            }
        }

        if (memberExpr.Member.DeclaringType == typeof(DBNull))
            return SqlSegment.Null;

        //各种类型的常量或是静态成员访问，如：DateTime.Now,int.MaxValue,string.Empty
        if (this.ormProvider.TryGetMemberAccessSqlFormatter(memberExpr.Member, out formatter))
            return sqlSegment.Change(formatter(null), false);

        //访问局部变量或是成员变量，当作常量处理,直接计算，如果是字符串变成参数@p
        //var orderIds=new List<int>{1,2,3}; Where(f=>orderIds.Contains(f.OrderId)); orderIds
        //private Order order; Where(f=>f.OrderId==this.Order.Id); this.Order.Id
        //var orderId=10; Select(f=>new {OrderId=orderId,...}
        //Select(f=>new {OrderId=this.Order.Id, ...}
        return this.EvaluateAndParameter(sqlSegment);
    }
    public virtual SqlSegment VisitConstant(SqlSegment sqlSegment)
    {
        var constantExpr = sqlSegment.Expression as ConstantExpression;
        if (constantExpr.Value == null)
            return SqlSegment.Null;

        return sqlSegment.Change(constantExpr.Value);
    }
    public virtual SqlSegment VisitMethodCall(SqlSegment sqlSegment)
    {
        var methodCallExpr = sqlSegment.Expression as MethodCallExpression;
        if (methodCallExpr.Method.DeclaringType == typeof(Sql)
            || typeof(IAggregateSelect).IsAssignableFrom(methodCallExpr.Method.DeclaringType))
            return this.VisitSqlMethodCall(sqlSegment);

        if (!this.ormProvider.TryGetMethodCallSqlFormatter(methodCallExpr.Method, out var formatter))
            throw new Exception($"不支持的方法访问，或是IOrmProvider未实现此方法{methodCallExpr.Method.Name}");

        object target = null;
        object[] args = null;

        //如果方法对象是聚合查询，不做任何处理
        if (methodCallExpr.Object != null)
            target = this.Visit(new SqlSegment { Expression = methodCallExpr.Object });

        //字符串连接单独特殊处理
        if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count > 0)
        {
            var arguments = new List<object>();
            foreach (var argumentExpr in methodCallExpr.Arguments)
            {
                arguments.Add(this.VisitAndDeferred(new SqlSegment { Expression = argumentExpr }));
            }
            args = arguments.ToArray();
        }
        var result = formatter.Invoke(target, sqlSegment.DeferredExprs, args);
        sqlSegment.IsExpression = true;
        return sqlSegment.Change(result, false);
    }
    public virtual SqlSegment VisitParameter(SqlSegment sqlSegment)
    {
        var parameterExpr = sqlSegment.Expression as ParameterExpression;
        var fromSegment = this.tableAlias[parameterExpr.Name];
        var readerFields = this.AddTableReaderFields(sqlSegment.ReaderIndex, fromSegment);
        return sqlSegment.Change(readerFields, false);
    }
    public virtual SqlSegment VisitNew(SqlSegment sqlSegment)
    {
        throw new NotImplementedException();
    }
    public virtual SqlSegment VisitMemberInit(SqlSegment sqlSegment)
    {
        throw new NotImplementedException();
    }
    public virtual SqlSegment VisitNewArray(SqlSegment sqlSegment)
    {
        var newArrayExpr = sqlSegment.Expression as NewArrayExpression;
        var result = new List<object>();
        foreach (var elementExpr in newArrayExpr.Expressions)
        {
            result.Add(this.Visit(new SqlSegment { Expression = elementExpr }));
        }
        return sqlSegment.Change(result);
    }
    public virtual SqlSegment VisitIndexExpression(SqlSegment sqlSegment)
    {
        var indexExpr = sqlSegment.Expression as IndexExpression;
        var argExpr = indexExpr.Arguments[0];
        var objIndex = argExpr is ConstantExpression constant
            ? constant.Value : this.Evaluate(sqlSegment.Next(argExpr)).Value;
        var index = (int)Convert.ChangeType(objIndex, typeof(int));
        var objTarget = this.Evaluate(sqlSegment.Next(indexExpr.Object)).Value;
        if (objTarget is List<object> objList)
            return sqlSegment.Change(objList[index]);
        throw new NotSupportedException("不支持的表达式: " + indexExpr);
    }
    public virtual SqlSegment VisitConditional(SqlSegment sqlSegment)
    {
        var conditionalExpr = sqlSegment.Expression as ConditionalExpression;
        sqlSegment = this.Visit(sqlSegment.Next(conditionalExpr.Test));
        var ifTrue = this.Visit(new SqlSegment { Expression = conditionalExpr.IfTrue });
        var ifFalse = this.Visit(new SqlSegment { Expression = conditionalExpr.IfFalse });
        if (sqlSegment.HasField && conditionalExpr.Test.Type == typeof(bool))
        {
            sqlSegment.Push(new DeferredExpr { OperationType = OperationType.Equal, Value = SqlSegment.True });
            sqlSegment = this.VisitBooleanDeferred(sqlSegment);
            return sqlSegment.Change($"CASE WHEN {sqlSegment} THEN {this.GetSqlValue(ifTrue)} ELSE {this.GetSqlValue(ifFalse)} END");
        }
        if (sqlSegment.Value is bool)
        {
            if ((bool)sqlSegment.Value)
                return sqlSegment.Change($"CASE WHEN {this.GetSqlValue(ifTrue)} THEN {this.ormProvider.GetQuotedValue(true)} ELSE {this.ormProvider.GetQuotedValue(false)} END");
            else return sqlSegment.Change($"CASE WHEN {this.GetSqlValue(ifFalse)} THEN {this.ormProvider.GetQuotedValue(true)} ELSE {this.ormProvider.GetQuotedValue(false)} END");
        }
        return sqlSegment;
    }
    public virtual SqlSegment VisitBooleanDeferred(SqlSegment fieldSegment)
    {
        if (!fieldSegment.HasDeferred)
            return fieldSegment;

        //处理HasValue !逻辑取反操作，这种情况下是一元操作
        int notIndex = 0;
        var sqlSegment = SqlSegment.None;
        var operationTypes = new OperationType[] { OperationType.Equal, OperationType.Not };

        while (fieldSegment.TryPop(operationTypes, out var deferredExpr))
        {
            if (deferredExpr.OperationType == OperationType.Equal)
            {
                sqlSegment = deferredExpr.Value as SqlSegment;
                break;
            }
            if (deferredExpr.OperationType == OperationType.Not)
                notIndex++;
        }
        if (sqlSegment == SqlSegment.None)
            sqlSegment = SqlSegment.True;

        string strOperator = null;
        if (notIndex % 2 > 0)
            strOperator = sqlSegment == SqlSegment.Null ? "IS NOT" : "<>";
        else strOperator = sqlSegment == SqlSegment.Null ? "IS" : "=";

        return fieldSegment.Merge(sqlSegment, $"{fieldSegment} {strOperator} {this.GetSqlValue(sqlSegment)}");
    }
    public virtual SqlSegment VisitConcatAndDeferred(SqlSegment sqlSegment)
    {
        var concatSegments = this.VisitConcatExpr(sqlSegment.Expression);
        bool isConstantValue = true;
        foreach (var segment in concatSegments)
        {
            if (!segment.IsConstantValue)
            {
                isConstantValue = false;
                break;
            }
        }
        if (isConstantValue)
            return sqlSegment.Change(string.Concat(concatSegments));
        var concatMethodInfo = typeof(string).GetMethod(nameof(string.Concat), new Type[] { typeof(object[]) });
        this.ormProvider.TryGetMethodCallSqlFormatter(concatMethodInfo, out var formater);
        return sqlSegment.Change(formater.Invoke(null, null, concatSegments), false);
    }
    public virtual List<SqlSegment> VisitConcatExpr(Expression concatExpr)
    {
        var completedSegements = new List<SqlSegment>();
        var deferredExprs = new Stack<Expression>();
        Func<Expression, bool> isConcatBinary = f =>
        {
            if (f is BinaryExpression binaryExpr && binaryExpr.NodeType == ExpressionType.Add
                && binaryExpr.Left.Type == typeof(string) && binaryExpr.Right.Type == typeof(string))
                return true;
            return false;
        };

        var nextExpr = concatExpr;
        while (true)
        {
            if (nextExpr is BinaryExpression binaryExpr)
            {
                var isLeftBinary = isConcatBinary(binaryExpr.Left);
                var isRightBinary = isConcatBinary(binaryExpr.Right);
                if (isLeftBinary)
                {
                    deferredExprs.Push(binaryExpr.Right);
                    nextExpr = binaryExpr.Left as BinaryExpression;
                    continue;
                }
                completedSegements.Add(this.Visit(new SqlSegment
                {
                    OperationType = OperationType.Concat,
                    Expression = binaryExpr.Left
                }));
                if (isRightBinary)
                {
                    nextExpr = binaryExpr.Right as BinaryExpression;
                    continue;
                }
                completedSegements.Add(this.Visit(new SqlSegment
                {
                    OperationType = OperationType.Concat,
                    Expression = binaryExpr.Right
                }));

                if (deferredExprs.TryPop(out var expr))
                    nextExpr = expr;
                else break;
            }
            else
            {
                completedSegements.Add(this.Visit(new SqlSegment
                {
                    OperationType = OperationType.Concat,
                    Expression = nextExpr
                }));
                if (deferredExprs.TryPop(out var expr))
                    nextExpr = expr;
                else break;
            }
        }
        return completedSegements;
    }
    public virtual List<SqlSegment> VisitLogicBinaryExpr(Expression expr)
    {
        Func<Expression, bool> isLogicBinary = f => f.NodeType != ExpressionType.AndAlso && f.NodeType != ExpressionType.OrElse;

        int deep = 0;
        var lastOperationType = OperationType.None;
        var deferredExprs = new Stack<SqlSegment>();
        var completedSegements = new List<SqlSegment>();
        var binaryExpr = expr as BinaryExpression;

        while (binaryExpr != null)
        {
            var operationType = binaryExpr.NodeType == ExpressionType.AndAlso ? OperationType.And : OperationType.Or;
            if (lastOperationType == OperationType.None)
                lastOperationType = operationType;
            if (operationType != lastOperationType)
                deep++;

            var isLeftLeaf = isLogicBinary(binaryExpr.Left);
            var isRightLeaf = isLogicBinary(binaryExpr.Right);
            if (isLeftLeaf)
            {
                var leftSegment = new SqlSegment
                {
                    OperationType = operationType,
                    Expression = binaryExpr.Left,
                    Deep = deep
                };
                if (binaryExpr.Left.NodeType == ExpressionType.MemberAccess)
                {
                    leftSegment.DeferredExprs ??= new();
                    leftSegment.DeferredExprs.Push(new DeferredExpr { OperationType = OperationType.Equal, Value = SqlSegment.True });
                }
                completedSegements.Add(leftSegment);
            }
            if (isRightLeaf)
            {
                var rightSegment = new SqlSegment
                {
                    OperationType = operationType,
                    Expression = binaryExpr.Right,
                    Deep = deep
                };
                if (binaryExpr.Right.NodeType == ExpressionType.MemberAccess)
                {
                    rightSegment.DeferredExprs ??= new();
                    rightSegment.DeferredExprs.Push(new DeferredExpr { OperationType = OperationType.Equal, Value = SqlSegment.True });
                }
                deferredExprs.Push(rightSegment);
            }
            Expression nextExpr = null;
            if (isLeftLeaf && isRightLeaf) break;
            if (isLeftLeaf && !isRightLeaf)
                nextExpr = binaryExpr.Right;
            if (!isLeftLeaf && isRightLeaf)
                nextExpr = binaryExpr.Left;

            binaryExpr = nextExpr as BinaryExpression;
        }
        while (deferredExprs.TryPop(out var sqlSegment))
        {
            completedSegements.Add(sqlSegment);
        }
        return completedSegements;
    }
    public virtual SqlSegment Evaluate(SqlSegment sqlSegment)
    {
        var member = Expression.Convert(sqlSegment.Expression, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(member);
        var getter = lambda.Compile();
        var objValue = getter();
        if (objValue == null)
            return SqlSegment.Null;
        return sqlSegment.Change(objValue);
    }
    public virtual SqlSegment EvaluateAndParameter(SqlSegment sqlSegment)
    {
        var member = Expression.Convert(sqlSegment.Expression, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(member);
        var getter = lambda.Compile();
        var objValue = getter();
        if (objValue == null)
            return SqlSegment.Null;

        //只有字符串会变成参数，有可能sql注入
        var type = sqlSegment.Expression.Type;
        if (type == typeof(string) || this.IsEnumerableString(type))
            return this.ToParameter(sqlSegment);
        return sqlSegment.Change(objValue);
    }
    public virtual SqlSegment VisitSqlMethodCall(SqlSegment sqlSegment)
    {
        var methodCallExpr = sqlSegment.Expression as MethodCallExpression;
        sqlSegment.IsExpression = true;
        sqlSegment.IsConstantValue = false;
        switch (methodCallExpr.Method.Name)
        {
            case "In":
                var elementType = methodCallExpr.Method.GetGenericArguments()[0];
                if (methodCallExpr.Arguments[1].Type.IsArray || typeof(IEnumerable<>).MakeGenericType(elementType).IsAssignableFrom(methodCallExpr.Arguments[1].Type))
                {
                    sqlSegment = this.Evaluate(sqlSegment.Next(methodCallExpr.Arguments[1]));
                    if (sqlSegment == SqlSegment.Null)
                        return sqlSegment.Change("0=1");
                    sqlSegment = this.ToParameter(sqlSegment);
                }
                else
                {
                    SqlSegment querySegment = null;
                    if (typeof(IQuery<>).MakeGenericType(elementType).IsAssignableFrom(methodCallExpr.Arguments[1].Type))
                        querySegment = this.Evaluate(sqlSegment.Next(methodCallExpr.Arguments[1]));
                    else querySegment = this.Visit(sqlSegment.Next(methodCallExpr.Arguments[1]));
                    var toSqlInvoker = this.BuildToSqlInvoker(elementType);
                    List<IDbDataParameter> dbDataParameters = null;
                    var sql = toSqlInvoker.Invoke(querySegment.Value, out dbDataParameters);
                    if (dbDataParameters != null && dbDataParameters.Count > 0)
                    {
                        this.dbParameters ??= new();
                        this.dbParameters.AddRange(dbDataParameters);
                    }
                    sqlSegment.Change(sql, false);
                }
                var fieldSegment = this.Visit(new SqlSegment { Expression = methodCallExpr.Arguments[0] });
                sqlSegment.Change($"{fieldSegment} IN ({sqlSegment})");
                break;
            case "Exists":
                var subTableTypes = methodCallExpr.Method.GetGenericArguments();
                var currentExpr = methodCallExpr.Arguments[0];
                while (currentExpr.NodeType != ExpressionType.Lambda)
                {
                    var unaryExpr = currentExpr as UnaryExpression;
                    currentExpr = unaryExpr.Operand;
                }
                var lambdaExpr = currentExpr as LambdaExpression;
                int index = 0;
                this.isNeedAlias = true;
                var builder = new StringBuilder("EXISTS(SELECT * FROM ");
                var removeIndices = new List<int>();
                foreach (var subTableType in subTableTypes)
                {
                    var subTableMapper = this.dbFactory.GetEntityMap(subTableType);
                    var aliasName = lambdaExpr.Parameters[index].Name;
                    var tableSegment = new TableSegment
                    {
                        EntityType = subTableType,
                        AliasName = aliasName
                    };
                    removeIndices.Add(this.tables.Count);
                    this.tables.Add(tableSegment);
                    this.tableAlias.Add(aliasName, tableSegment);
                    if (index > 0) builder.Append(',');
                    builder.Append(this.ormProvider.GetTableName(subTableMapper.TableName));
                    builder.Append($" {aliasName}");
                    index++;
                }
                builder.Append(" WHERE ");
                builder.Append(this.VisitConditionExpr(lambdaExpr.Body));
                builder.Append(')');
                removeIndices.Reverse();
                removeIndices.ForEach(f => this.tables.RemoveAt(f));
                sqlSegment.Change(builder.ToString(), false);
                break;
            case "Count":
            case "LongCount":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"COUNT({sqlSegment})", false);
                }
                else sqlSegment.Change("COUNT(1)", false);
                this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "CountDistinct":
            case "LongCountDistinct":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"COUNT(DISTINCT {sqlSegment})", false);
                }
                this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Sum":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"SUM({sqlSegment})", false);
                }
                this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Avg":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"AVG({sqlSegment})", false);
                }
                this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Max":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"MAX({sqlSegment})", false);
                }
                this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Min":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"MIN({sqlSegment})", false);
                }
                this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
        }
        return sqlSegment;
    }
    public virtual SqlSegment VisitFromQuery(SqlSegment sqlSegment, Type elementType)
    {
        var lambdaExpr = sqlSegment.Expression as LambdaExpression;
        var fromQuery = new FromQuery(this.dbFactory, this.connection, this.transaction);
        var queryInvoker = lambdaExpr.Compile();
        var subQuery = queryInvoker.DynamicInvoke(fromQuery);
        return sqlSegment.Change(subQuery);
    }
    public virtual string VisitConditionExpr(Expression conditionExpr)
    {
        if (conditionExpr.NodeType == ExpressionType.AndAlso || conditionExpr.NodeType == ExpressionType.OrElse)
        {
            int lastDeep = 0;
            var builder = new StringBuilder();
            var sqlSegments = this.VisitLogicBinaryExpr(conditionExpr);
            for (int i = 0; i < sqlSegments.Count; i++)
            {
                var sqlSegment = this.VisitAndDeferred(sqlSegments[i]);
                var separator = sqlSegment.OperationType == OperationType.And ? " AND " : " OR ";
                if (i > 0)
                {
                    if (sqlSegment.Deep < lastDeep)
                    {
                        var loopTimes = lastDeep - sqlSegment.Deep;
                        for (int j = 0; j < loopTimes; j++)
                        {
                            builder.Append(')');
                        }
                    }
                    builder.Append(separator);
                }
                if (i == 0)
                {
                    if (sqlSegment.Deep > 0)
                    {
                        for (int j = 0; j < sqlSegment.Deep; j++)
                        {
                            builder.Append('(');
                        }
                    }
                }
                else
                {
                    if (sqlSegment.Deep > lastDeep)
                    {
                        var loopTimes = sqlSegment.Deep - lastDeep;
                        for (int j = 0; j < loopTimes; j++)
                        {
                            builder.Append('(');
                        }
                    }
                }
                builder.Append(sqlSegment.ToString());
                lastDeep = sqlSegment.Deep;
            }
            return builder.ToString();
        }
        return this.VisitAndDeferred(new SqlSegment { Expression = conditionExpr }).ToString();
    }
    public virtual string GetSqlValue(SqlSegment sqlSegment)
    {
        if (sqlSegment == SqlSegment.Null || !sqlSegment.IsConstantValue)
            return sqlSegment.ToString();
        return this.ormProvider.GetQuotedValue(sqlSegment.Value);
    }
    public virtual TableSegment FindTableSegment(string parameterName, string path)
    {
        var index = path.IndexOf(".");
        if (index > 0)
        {
            var rootTableSegment = this.tableAlias[parameterName];
            path = path.Replace(parameterName + ".", rootTableSegment.AliasName + ".");
            return this.tables.Find(f => f.Path == path);
        }
        else return this.tableAlias[parameterName];
    }
    public virtual List<ReaderField> AddTableReaderFields(int readerIndex, TableSegment fromSegment)
    {
        var readerFields = new List<ReaderField>();
        fromSegment.Mapper ??= this.dbFactory.GetEntityMap(fromSegment.EntityType);
        var lastReaderField = new ReaderField
        {
            Index = readerIndex,
            Type = ReaderFieldType.Entity,
            TableSegment = fromSegment
            //最外层Select对象的成员，位于顶层, FromMember暂时不设置值，到Select时候去设置 
        };
        readerFields.Add(lastReaderField);
        this.AddIncludeTables(lastReaderField, readerFields);
        return readerFields;
    }
    public virtual SqlSegment ToParameter(SqlSegment sqlSegment)
    {
        if (sqlSegment == SqlSegment.Null)
            return SqlSegment.Null;
        sqlSegment.IsParameter = true;
        this.dbParameters ??= new();
        if (sqlSegment.Value is IEnumerable objValues && sqlSegment.Value.GetType() != typeof(string))
        {
            string paramPrefix = null;
            if (!string.IsNullOrEmpty(sqlSegment.ParameterName))
                paramPrefix = sqlSegment.ParameterName;
            else paramPrefix = this.ormProvider.ParameterPrefix + this.parameterPrefix;
            int index = 0;
            var builder = new StringBuilder();
            foreach (var objValue in objValues)
            {
                if (index > 0) builder.Append(',');
                var parameterName = paramPrefix + index.ToString();
                builder.Append(parameterName);
                this.dbParameters.Add(this.ormProvider.CreateParameter(parameterName, objValue));
                index++;
            }
            return sqlSegment.Change(builder.ToString(), false);
        }
        else
        {
            if (!string.IsNullOrEmpty(sqlSegment.ParameterName))
                return sqlSegment.Change(sqlSegment.ParameterName, false);
            var parameterName = this.ormProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();
            return sqlSegment.Change(parameterName, false);
        }
    }
    private void AddIncludeTables(ReaderField lastReaderField, List<ReaderField> readerFields)
    {
        var includedSegments = this.tables.FindAll(f => f.IsInclude && f.FromTable == lastReaderField.TableSegment);
        if (includedSegments != null && includedSegments.Count > 0)
        {
            lastReaderField.HasNextInclude = true;
            foreach (var includedSegment in includedSegments)
            {
                var readerField = new ReaderField
                {
                    Index = readerFields.Count,
                    Type = ReaderFieldType.Entity,
                    TableSegment = includedSegment,
                    FromMember = includedSegment.FromMember.Member,
                    TargetMember = includedSegment.FromMember.Member,
                    ParentIndex = lastReaderField.Index
                };
                readerFields.Add(readerField);
                if (this.tables.Exists(f => f.IsInclude && f.FromTable == includedSegment))
                    this.AddIncludeTables(readerField, readerFields);
            }
        }
    }
    private ToSqlDelegate BuildToSqlInvoker(Type elementType)
    {
        if (!toSqlCache.TryGetValue(elementType, out var toSqlInvoker))
        {
            var queryType = typeof(IQuery<>).MakeGenericType(elementType);
            var parameterExpr = Expression.Parameter(typeof(object), "obj");
            var dbParametersExpr = Expression.Parameter(typeof(List<IDbDataParameter>).MakeByRefType(), "dbParameters");
            var resultLabelExpr = Expression.Label(typeof(string));

            var methodInfo = queryType.GetMethod("ToSql");
            var convertExpr = Expression.Convert(parameterExpr, queryType);
            var toSqlExpr = Expression.Call(convertExpr, methodInfo, dbParametersExpr);
            var returnExpr = Expression.Return(resultLabelExpr, toSqlExpr);
            var labelExpr = Expression.Label(resultLabelExpr, Expression.Default(typeof(string)));
            var bodyExpr = Expression.Block(returnExpr, labelExpr);
            toSqlInvoker = Expression.Lambda<ToSqlDelegate>(bodyExpr, parameterExpr, dbParametersExpr).Compile();
            toSqlCache.TryAdd(elementType, toSqlInvoker);
        }
        return toSqlInvoker;
    }
    private void Swap<T>(ref T left, ref T right)
    {
        var temp = right;
        right = left;
        left = temp;
    }
    private string GetOperator(ExpressionType exprType)
    {
        switch (exprType)
        {
            case ExpressionType.Equal: return "=";
            case ExpressionType.NotEqual: return "<>";
            case ExpressionType.GreaterThan: return ">";
            case ExpressionType.GreaterThanOrEqual: return ">=";
            case ExpressionType.LessThan: return "<";
            case ExpressionType.LessThanOrEqual: return "<=";
            case ExpressionType.AndAlso: return "AND";
            case ExpressionType.OrElse: return "OR";
            case ExpressionType.Add: return "+";
            case ExpressionType.Subtract: return "-";
            case ExpressionType.Multiply: return "*";
            case ExpressionType.Divide: return "/";
            case ExpressionType.Modulo: return "MOD";
            case ExpressionType.Coalesce: return "COALESCE";
            case ExpressionType.And: return "&";
            case ExpressionType.Or: return "|";
            case ExpressionType.ExclusiveOr: return "^";
            case ExpressionType.LeftShift: return "<<";
            case ExpressionType.RightShift: return ">>";
            default: return exprType.ToString();
        }
    }
    private bool IsEnumerableString(Type type)
    {
        if (!typeof(IEnumerable).IsAssignableFrom(type))
            return false;
        var genericArguments = type.GetGenericArguments();
        if (genericArguments == null || genericArguments.Length <= 0)
            return false;
        if (genericArguments[0] == typeof(string))
            return true;
        return false;
    }
    private bool IsFromQueryLambda(LambdaExpression lambdaExpr, out Type elementType)
    {
        elementType = null;
        if (lambdaExpr.Parameters == null || lambdaExpr.Parameters.Count != 1)
            return false;
        if (lambdaExpr.Parameters[0].Type != typeof(IFromQuery))
            return false;
        if (!lambdaExpr.ReturnType.IsGenericType)
            return false;
        var genericArguments = lambdaExpr.ReturnType.GetGenericArguments();
        if (genericArguments == null || genericArguments.Length != 1)
            return false;
        if (!typeof(IQuery<>).MakeGenericType(genericArguments[0]).IsAssignableFrom(lambdaExpr.ReturnType))
            return false;
        elementType = genericArguments[0];
        return true;
    }
}
