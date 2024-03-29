﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Thea.Orm;

namespace Thea.Trolley;

public class SqlVisitor : ISqlVisitor
{
    protected internal readonly string dbKey;
    protected readonly IEntityMapProvider mapProvider;

    protected string parameterPrefix = "p";
    protected List<TableSegment> tables;
    protected Dictionary<string, TableSegment> tableAlias;
    protected List<IDbDataParameter> dbParameters;
    protected List<ReaderField> readerFields;
    protected bool isSelect = false;
    protected bool isWhere = false;
    protected bool isFromQuery = false;
    protected OperationType lastWhereNodeType = OperationType.None;
    public bool IsParameterized { get; set; }
    public virtual char TableAsStart { get; set; }
    public virtual bool IsNeedAlias { get; set; }
    public virtual IOrmProvider OrmProvider { get; private set; }

    public SqlVisitor(string dbKey, IOrmProvider ormProvider, IEntityMapProvider mapProvider, bool isParameterized = false, char tableAsStart = 'a', string parameterPrefix = "p")
    {
        this.dbKey = dbKey;
        this.OrmProvider = ormProvider;
        this.mapProvider = mapProvider;
        this.IsParameterized = isParameterized;
        this.TableAsStart = tableAsStart;
        this.parameterPrefix = parameterPrefix;
    }
    public virtual SqlSegment VisitAndDeferred(SqlSegment sqlSegment)
    {
        sqlSegment = this.Visit(sqlSegment);
        if (!sqlSegment.HasDeferred)
            return sqlSegment;

        //处理HasValue !逻辑取反操作，这种情况下是一元操作
        return this.VisitDeferredBoolConditional(sqlSegment, true, this.OrmProvider.GetQuotedValue(true), this.OrmProvider.GetQuotedValue(false));
    }
    public virtual SqlSegment Visit(SqlSegment sqlSegment)
    {
        SqlSegment result = null;
        if (sqlSegment.Expression == null)
            throw new ArgumentNullException("sqlSegment.Expression");

        switch (sqlSegment.Expression.NodeType)
        {
            case ExpressionType.Lambda:
                var lambdaExpr = sqlSegment.Expression as LambdaExpression;
                result = this.Visit(sqlSegment.Next(lambdaExpr.Body));
                break;
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
            case ExpressionType.ListInit:
                result = this.VisitListInit(sqlSegment);
                break;
            case ExpressionType.TypeIs:
                result = this.VisitTypeIs(sqlSegment);
                break;
            default: throw new ArgumentNullException($"不支持的表达式操作，{sqlSegment.Expression}");
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
                    //SELECT/WHERE语句，都会有Defer处理，在最外层再计算bool值
                    sqlSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                    return this.Visit(sqlSegment.Next(unaryExpr.Operand));
                }
                return sqlSegment.ChangeValue($"~{this.Visit(sqlSegment)}");
            case ExpressionType.Convert:
                if (unaryExpr.Method != null)
                {
                    if (unaryExpr.Operand.IsParameter(out _))
                        return this.Visit(sqlSegment.Next(unaryExpr.Operand));
                    return this.Evaluate(sqlSegment);
                }
                //string.Concat,string.Format,string.Join等方法，参数都是object，
                //最终变成string，字段访问、参数、表达式、方法调用需要强制转换,如：a.IntField + 5, b.Field等，常量不需要强转
                //整形，日期，布尔，...等等常量或变量当作object类型处理的，
                sqlSegment = this.Visit(sqlSegment.Next(unaryExpr.Operand));
                //if (sqlSegment.TargetType != null && sqlSegment.Type != sqlSegment.TargetType && !sqlSegment.IsConstant && !sqlSegment.IsVariable)
                //{
                //    var memberMapper = sqlSegment.MemberMapper;
                //    if (memberMapper == null)
                //    {
                //        sqlSegment.TableSegment.Mapper ??= this.mapProvider.GetEntityMap(sqlSegment.TableSegment.EntityType);
                //        memberMapper = sqlSegment.TableSegment.Mapper.GetMemberMap(sqlSegment.FromMember.Name);
                //    }
                //    if (this.OrmProvider.MapDefaultType(memberMapper.NativeDbType) != sqlSegment.TargetType)
                //        sqlSegment.Value = this.OrmProvider.CastTo(sqlSegment.TargetType, this.GetQuotedValue(sqlSegment));
                //    if (sqlSegment.Type != sqlSegment.TargetType)
                //        sqlSegment.Type = sqlSegment.TargetType;
                //}
                return sqlSegment;
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
                if (this.IsStringConcatOperator(sqlSegment, out var operatorSegment))
                    return operatorSegment;
                if (this.IsDateTimeOperator(sqlSegment, out operatorSegment))
                    return operatorSegment;
                if (this.IsTimeSpanOperator(sqlSegment, out operatorSegment))
                    return operatorSegment;

                var leftSegment = this.Visit(sqlSegment.Next(binaryExpr.Left));
                var rightSegment = this.Visit(leftSegment.Clone(binaryExpr.Right));

                //计算数组访问，a??bb
                if (leftSegment.IsConstant && rightSegment.IsConstant)
                    return this.Evaluate(sqlSegment.Next(binaryExpr));

                if ((leftSegment.IsConstant || leftSegment.IsVariable)
                    && (rightSegment.IsConstant || rightSegment.IsVariable))
                {
                    this.Evaluate(sqlSegment.Next(binaryExpr));
                    sqlSegment.IsConstant = false;
                    sqlSegment.IsVariable = true;
                    return sqlSegment;
                }

                if (!leftSegment.HasField && rightSegment.HasField)
                {
                    this.Swap(ref leftSegment, ref rightSegment);
                    rightSegment = this.Visit(leftSegment.Clone(binaryExpr.Right));
                }

                //bool类型的表达式，这里不做解析，到where、having、joinOn子句中去解析并展开合并
                if (binaryExpr.NodeType == ExpressionType.Equal || binaryExpr.NodeType == ExpressionType.NotEqual)
                {
                    //处理!(a.IsEnabled==true)情况,bool类型，最外层再做defer处理
                    if (binaryExpr.Left.Type == typeof(bool) && leftSegment.HasField && !rightSegment.HasField)
                    {
                        leftSegment.Push(new DeferredExpr { OperationType = OperationType.Equal, Value = SqlSegment.True });
                        if (!(bool)rightSegment.Value)
                            leftSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                        if (binaryExpr.NodeType == ExpressionType.NotEqual)
                            leftSegment.Push(new DeferredExpr { OperationType = OperationType.Not });
                        return leftSegment;
                    }
                    //处理a.UserName!=null情况
                    if (leftSegment == SqlSegment.Null && rightSegment != SqlSegment.Null)
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

                var operators = this.OrmProvider.GetBinaryOperator(binaryExpr.NodeType);
                string strLeft = this.GetQuotedValue(this.Change(leftSegment));
                string strRight = this.GetQuotedValue(this.Change(rightSegment));
                if (binaryExpr.NodeType == ExpressionType.Coalesce)
                {
                    leftSegment.IsFieldType = true;
                    return this.Merge(leftSegment, rightSegment, $"{operators}({strLeft},{strRight})", false, true);
                }

                if (leftSegment.IsExpression)
                    strLeft = $"({strLeft})";
                if (rightSegment.IsExpression)
                    strRight = $"({strRight})";
                return this.Merge(leftSegment, rightSegment, $"{strLeft}{operators}{strRight}", true, false);
        }
        return sqlSegment;
    }
    public virtual SqlSegment VisitMemberAccess(SqlSegment sqlSegment)
    {
        throw new NotImplementedException();
    }
    public virtual SqlSegment VisitConstant(SqlSegment sqlSegment)
    {
        var constantExpr = sqlSegment.Expression as ConstantExpression;
        if (constantExpr.Value == null)
            return SqlSegment.Null;

        sqlSegment.Value = constantExpr.Value;
        sqlSegment.IsConstant = true;
        return sqlSegment;
    }
    public virtual SqlSegment VisitMethodCall(SqlSegment sqlSegment)
    {
        var methodCallExpr = sqlSegment.Expression as MethodCallExpression;
        if (methodCallExpr.Method.DeclaringType == typeof(Sql)
            || typeof(IAggregateSelect).IsAssignableFrom(methodCallExpr.Method.DeclaringType))
            return this.VisitSqlMethodCall(sqlSegment);

        if (!sqlSegment.IsDeferredFields && this.OrmProvider.TryGetMethodCallSqlFormatter(methodCallExpr, out var formatter))
            return formatter.Invoke(this, methodCallExpr, methodCallExpr.Object, sqlSegment.DeferredExprs, methodCallExpr.Arguments.ToArray());

        string fields = null;
        List<ReaderField> readerFields = null;
        List<Expression> constArgs = null;
        if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count > 0)
        {
            readerFields = new List<ReaderField>();
            constArgs = new List<Expression>();
            var builder = new StringBuilder();
            for (int i = 0; i < methodCallExpr.Arguments.Count; i++)
            {
                var argumentSegment = this.VisitAndDeferred(new SqlSegment { Expression = methodCallExpr.Arguments[i] });
                if (argumentSegment.HasField)
                {
                    sqlSegment.Merge(argumentSegment);
                    var fieldName = argumentSegment.Value.ToString();
                    readerFields.Add(new ReaderField
                    {
                        Index = i,
                        FieldType = ReaderFieldType.Field,
                        TableSegment = argumentSegment.TableSegment,
                        FromMember = argumentSegment.FromMember,
                        Body = fieldName
                    });
                    if (builder.Length > 0)
                        builder.Append(',');
                    builder.Append(fieldName);
                }
                else constArgs.Add(Expression.Constant(argumentSegment.Value));
            }
            if (readerFields.Count > 0)
                fields = builder.ToString();
        }

        if (sqlSegment.IsDeferredFields || !string.IsNullOrEmpty(fields))
        {
            if (readerFields == null)
                fields = "NULL";
            return sqlSegment.Change(new ReaderField
            {
                FieldType = ReaderFieldType.DeferredFields,
                Body = fields,
                DeferCallTarget = methodCallExpr.Object,
                DeferCallMethod = methodCallExpr.Method,
                DeferCallArgs = constArgs,
                ReaderFields = readerFields
            }, false, false, true);
        }
        return this.Evaluate(sqlSegment);
    }
    public virtual SqlSegment VisitParameter(SqlSegment sqlSegment)
    {
        var parameterExpr = sqlSegment.Expression as ParameterExpression;

        if (this.isFromQuery)
            throw new NotSupportedException($"FROM子查询中不支持实体类型成员MemberAccess表达式访问，只支持基础字段访问访问,{parameterExpr}");

        var fromSegment = this.tableAlias[parameterExpr.Name];
        //参数访问的是模型，通常是成员访问或是SELECT语句的实体访问，成员访问已经处理了参数访问，不会走到此方法
        //参数访问通常都是SELECT语句的实体访问
        if (!this.isSelect) throw new NotSupportedException($"不支持的参数表达式访问，{parameterExpr}");

        var readerFields = this.AddTableRecursiveReaderFields(sqlSegment.ReaderIndex, fromSegment);
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
        sqlSegment.IsArray = true;
        var newArrayExpr = sqlSegment.Expression as NewArrayExpression;
        var result = new List<SqlSegment>();
        bool isConstantValue = true;
        foreach (var elementExpr in newArrayExpr.Expressions)
        {
            var elementSegment = sqlSegment.Clone(elementExpr);
            elementSegment = this.VisitAndDeferred(elementSegment);
            if (!elementSegment.IsConstant)
                isConstantValue = false;
            sqlSegment.Merge(elementSegment);
            result.Add(elementSegment);
        }
        return sqlSegment.Change(result, isConstantValue);
    }
    public virtual SqlSegment VisitIndexExpression(SqlSegment sqlSegment)
    {
        if (sqlSegment.Expression.IsParameter(out _))
            throw new NotSupportedException("索引表达式不支持Parameter访问操作");
        return this.Evaluate(sqlSegment);
    }
    public virtual SqlSegment VisitConditional(SqlSegment sqlSegment)
    {
        var conditionalExpr = sqlSegment.Expression as ConditionalExpression;
        sqlSegment = this.Visit(sqlSegment.Next(conditionalExpr.Test));
        var ifTrueSegment = this.Visit(new SqlSegment { Expression = conditionalExpr.IfTrue });
        var leftArgument = this.GetQuotedValue(ifTrueSegment);

        if (sqlSegment.MemberMapper != null)
        {
            //三元条件表达式，通常都会改变原表达式的类型，类型相同则继续使用MemberMapper
            sqlSegment.MemberMapper = null;
            sqlSegment.Type = null;
            sqlSegment.ExpectType = null;
            sqlSegment.TargetType = null;
        }
        SqlSegment ifFalseSegment = null;
        if (ifTrueSegment.HasField && (!ifTrueSegment.IsExpression && !ifTrueSegment.IsMethodCall || ifTrueSegment.IsFieldType))
        {
            ifFalseSegment = this.Visit(ifTrueSegment.Clone(conditionalExpr.IfFalse));
            sqlSegment.MemberMapper = ifTrueSegment.MemberMapper;
            sqlSegment.IsFieldType = true;
        }
        else ifFalseSegment = this.Visit(new SqlSegment { Expression = conditionalExpr.IfFalse });
        var rightArgument = this.GetQuotedValue(ifFalseSegment);
        sqlSegment.Merge(ifTrueSegment);
        sqlSegment.Merge(ifFalseSegment);
        return this.VisitDeferredBoolConditional(sqlSegment, conditionalExpr.IfTrue.Type == typeof(bool), leftArgument, rightArgument);
    }
    public virtual SqlSegment VisitListInit(SqlSegment sqlSegment)
    {
        sqlSegment.IsArray = true;
        var listExpr = sqlSegment.Expression as ListInitExpression;
        var result = new List<SqlSegment>();
        bool isConstantValue = true;
        foreach (var elementInit in listExpr.Initializers)
        {
            if (elementInit.Arguments.Count == 0)
                continue;
            var elementSegment = sqlSegment.Clone(elementInit.Arguments[0]);
            elementSegment = this.VisitAndDeferred(elementSegment);
            if (!elementSegment.IsConstant)
                isConstantValue = false;
            result.Add(elementSegment);
        }
        return sqlSegment.Change(result, isConstantValue);
    }
    public virtual SqlSegment VisitTypeIs(SqlSegment sqlSegment)
    {
        var binaryExpr = sqlSegment.Expression as TypeBinaryExpression;
        if (!binaryExpr.Expression.IsParameter(out _))
            return this.Evaluate(sqlSegment);
        if (binaryExpr.TypeOperand == typeof(DBNull))
        {
            sqlSegment.Push(new DeferredExpr
            {
                OperationType = OperationType.Equal,
                Value = SqlSegment.Null
            });
            return this.Visit(sqlSegment.Next(binaryExpr.Expression));
        }
        throw new NotSupportedException($"不支持的表达式操作，{sqlSegment.Expression}");
    }
    public virtual SqlSegment Evaluate(SqlSegment sqlSegment)
    {
        var lambdaExpr = Expression.Lambda(sqlSegment.Expression);
        var objValue = lambdaExpr.Compile().DynamicInvoke();
        if (objValue == null)
            return SqlSegment.Null;

        return sqlSegment.Change(objValue);
    }
    public virtual object Evaluate(Expression expr)
    {
        var lambdaExpr = Expression.Lambda(expr);
        return lambdaExpr.Compile().DynamicInvoke();
    }
    public virtual T Evaluate<T>(Expression expr)
    {
        var lambdaExpr = Expression.Lambda(expr);
        var objValue = lambdaExpr.Compile().DynamicInvoke();
        if (objValue == null)
            return default;
        return (T)objValue;
    }
    public virtual SqlSegment VisitSqlMethodCall(SqlSegment sqlSegment)
    {
        var methodCallExpr = sqlSegment.Expression as MethodCallExpression;
        LambdaExpression lambdaExpr = null;
        switch (methodCallExpr.Method.Name)
        {
            case "FlattenTo"://通常在最外层的SELECT中转为其他类型
                var targetType = methodCallExpr.Method.ReturnType;
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count > 0)
                {
                    lambdaExpr = sqlSegment.OriginalExpression as LambdaExpression;
                    var visitedParameters = lambdaExpr.Parameters;
                    lambdaExpr = this.EnsureLambda(methodCallExpr.Arguments[0]);
                    lambdaExpr = Expression.Lambda(lambdaExpr.Body, visitedParameters);
                }
                var readerFields = this.FlattenFieldsTo(targetType, lambdaExpr);
                sqlSegment.ChangeValue(readerFields);
                break;
            case "Deferred":
                sqlSegment.IsDeferredFields = true;
                sqlSegment = this.VisitMethodCall(sqlSegment.Next(methodCallExpr.Arguments[0]));
                break;
            case "IsNull":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count > 0)
                {
                    sqlSegment.Push(new DeferredExpr { OperationType = OperationType.Equal, Value = SqlSegment.Null });
                    sqlSegment = this.VisitAndDeferred(sqlSegment.Next(methodCallExpr.Arguments[0]));
                }
                break;
            case "ToParameter":
                sqlSegment.IsParameterized = true;
                sqlSegment = this.Change(this.Visit(sqlSegment.Next(methodCallExpr.Arguments[0])));
                sqlSegment.IsParameterized = false;
                break;
            case "In":
                var elementType = methodCallExpr.Method.GetGenericArguments()[0];
                var type = methodCallExpr.Arguments[1].Type;
                var fieldSegment = this.Visit(new SqlSegment { Expression = methodCallExpr.Arguments[0] });
                if (type.IsArray || typeof(IEnumerable<>).MakeGenericType(elementType).IsAssignableFrom(type))
                {
                    var rightSegment = this.VisitAndDeferred(fieldSegment.Clone(methodCallExpr.Arguments[1]));
                    if (rightSegment == SqlSegment.Null)
                        return sqlSegment.Change("1=0", false, true, false);
                    var enumerable = rightSegment.Value as IEnumerable;

                    var builder = new StringBuilder();
                    foreach (var item in enumerable)
                    {
                        if (builder.Length > 0) builder.Append(',');
                        builder.Append(this.OrmProvider.GetQuotedValue(item));
                    }
                    sqlSegment.ChangeValue(builder.ToString());
                }
                else
                {
                    lambdaExpr = methodCallExpr.Arguments[1] as LambdaExpression;
                    var sql = this.VisitFromQuery(lambdaExpr, out var isNeedAlias);
                    sqlSegment.ChangeValue(sql);
                    if (isNeedAlias)
                    {
                        //重新解析一下，增加别名
                        if (!this.IsNeedAlias)
                        {
                            this.IsNeedAlias = true;
                            fieldSegment = this.Visit(fieldSegment.Next(methodCallExpr.Arguments[0]));
                        }
                    }
                }
                sqlSegment.Change($"{fieldSegment} IN ({sqlSegment})", false, true, false);
                break;
            case "Exists":
                lambdaExpr = this.EnsureLambda(methodCallExpr.Arguments[0]);
                this.IsNeedAlias = true;
                string existsSql = null;
                var subTableTypes = methodCallExpr.Method.GetGenericArguments();
                List<IDbDataParameter> parameters = null;
                if (subTableTypes != null && subTableTypes.Length > 0)
                {
                    var removeIndices = new List<int>();
                    var builder = new StringBuilder("SELECT * FROM ");
                    int index = 0;
                    foreach (var subTableType in subTableTypes)
                    {
                        var subTableMapper = this.mapProvider.GetEntityMap(subTableType);
                        var aliasName = lambdaExpr.Parameters[index].Name;
                        var tableSegment = new TableSegment
                        {
                            EntityType = subTableType,
                            AliasName = aliasName
                        };
                        removeIndices.Add(this.tables.Count);
                        this.tables.Add(tableSegment);
                        this.tableAlias[aliasName] = tableSegment;
                        if (index > 0) builder.Append(',');
                        builder.Append(this.OrmProvider.GetTableName(subTableMapper.TableName));
                        builder.Append($" {tableSegment.AliasName}");
                        index++;
                    }
                    builder.Append(" WHERE ");
                    builder.Append(this.VisitConditionExpr(lambdaExpr.Body));
                    removeIndices.Reverse();
                    removeIndices.ForEach(f => this.tables.RemoveAt(f));
                    existsSql = builder.ToString();
                }
                else existsSql = this.VisitFromQuery(lambdaExpr, out _);
                if (parameters != null && parameters.Count > 0)
                    this.dbParameters.AddRange(parameters);
                sqlSegment.Change($"EXISTS({existsSql})", false, false, true);
                break;
            case "Count":
            case "LongCount":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");

                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"COUNT({sqlSegment})", false, false, true);
                }
                else sqlSegment.Change("COUNT(1)", false, false, true);
                if (this.isSelect || this.isWhere)
                    this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "CountDistinct":
            case "LongCountDistinct":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");

                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"COUNT(DISTINCT {sqlSegment})", false, false, true);
                }
                if (this.isSelect || this.isWhere)
                    this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Sum":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"SUM({sqlSegment})", false, false, true);
                }
                if (this.isSelect || this.isWhere)
                    this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Avg":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"AVG({sqlSegment})", false, false, true);
                }
                if (this.isSelect || this.isWhere)
                    this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Max":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"MAX({sqlSegment})", false, false, true);
                }
                if (this.isSelect || this.isWhere)
                    this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
            case "Min":
                if (methodCallExpr.Arguments != null && methodCallExpr.Arguments.Count == 1)
                {
                    if (methodCallExpr.Arguments[0].NodeType != ExpressionType.MemberAccess)
                        throw new NotSupportedException("不支持的表达式，只支持MemberAccess类型表达式");
                    sqlSegment = this.VisitMemberAccess(sqlSegment.Next(methodCallExpr.Arguments[0]));
                    sqlSegment.Change($"MIN({sqlSegment})", false, false, true);
                }
                if (this.isSelect || this.isWhere)
                    this.tables.FindAll(f => f.IsMaster).ForEach(f => f.IsUsed = true);
                break;
        }
        return sqlSegment;
    }
    public virtual bool IsStringConcatOperator(SqlSegment sqlSegment, out SqlSegment result)
    {
        var binaryExpr = sqlSegment.Expression as BinaryExpression;
        if (binaryExpr.NodeType == ExpressionType.Add && (binaryExpr.Left.Type == typeof(string) || binaryExpr.Right.Type == typeof(string)))
        {
            //先打开所有要拼接的部分，最后再拼接
            var concatExprs = this.SplitConcatList(sqlSegment.Expression);
            //调用拼接方法Concat,每个数据库Provider都实现了这个方法
            var methodInfo = typeof(string).GetMethod(nameof(string.Concat), new Type[] { typeof(object[]) });
            var parameters = Expression.NewArrayInit(typeof(object), concatExprs);
            var methodCallExpr = Expression.Call(methodInfo, parameters);
            sqlSegment.Expression = methodCallExpr;
            this.OrmProvider.TryGetMethodCallSqlFormatter(methodCallExpr, out var formater);
            //返回的SQL表达式中直接拼接好          
            result = formater.Invoke(this, binaryExpr, null, null, concatExprs);
            return true;
        }
        result = null;
        return false;
    }
    public virtual string VisitConditionExpr(Expression conditionExpr)
    {
        if (conditionExpr.NodeType == ExpressionType.AndAlso || conditionExpr.NodeType == ExpressionType.OrElse)
        {
            var completedExprs = this.VisitLogicBinaryExpr(conditionExpr);
            if (conditionExpr.NodeType == ExpressionType.OrElse)
                this.lastWhereNodeType = OperationType.Or;
            else this.lastWhereNodeType = OperationType.And;

            var builder = new StringBuilder();
            foreach (var completedExpr in completedExprs)
            {
                if (completedExpr.ExpressionType == ConditionType.OperatorType)
                {
                    builder.Append(completedExpr.Body);
                    continue;
                }
                var sqlSegment = this.VisitAndDeferred(this.CreateConditionSegment(completedExpr.Body as Expression));
                builder.Append(sqlSegment);
            }
            return builder.ToString();
        }
        return this.VisitAndDeferred(this.CreateConditionSegment(conditionExpr)).ToString();
    }
    public virtual List<Expression> ConvertFormatToConcatList(Expression[] argsExprs)
    {
        var format = this.Evaluate<string>(argsExprs[0]);
        int index = 1, formatIndex = 0;
        var parameters = new List<Expression>();
        for (int i = 1; i < argsExprs.Length; i++)
        {
            switch (argsExprs[i].NodeType)
            {
                case ExpressionType.ListInit:
                    var listExpr = argsExprs[i] as ListInitExpression;
                    foreach (var elementInit in listExpr.Initializers)
                    {
                        if (elementInit.Arguments.Count == 0)
                            continue;
                        parameters.Add(elementInit.Arguments[0]);
                    }
                    break;
                case ExpressionType.NewArrayBounds:
                case ExpressionType.NewArrayInit:
                    var newArrayExpr = argsExprs[i] as NewArrayExpression;
                    foreach (var elementExpr in newArrayExpr.Expressions)
                    {
                        parameters.Add(elementExpr);
                    }
                    break;
                default: parameters.Add(argsExprs[i]); break;
            }
        }
        index = 0;
        var result = new List<Expression>();
        while (formatIndex < format.Length)
        {
            var nextIndex = format.IndexOf('{', formatIndex);
            if (nextIndex > formatIndex)
            {
                var constValue = format.Substring(formatIndex, nextIndex - formatIndex);
                result.Add(Expression.Constant(constValue));
            }
            result.AddRange(this.SplitConcatList(parameters[index]));
            index++;
            formatIndex = format.IndexOf('}', nextIndex + 2) + 1;
        }
        return result;
    }
    public virtual List<Expression> SplitConcatList(Expression[] argsExprs)
    {
        var completedExprs = new List<Expression>();
        var deferredExprs = new Stack<Expression>();
        Func<Expression, bool> isConcatBinary = f =>
        {
            if (f is BinaryExpression binaryExpr && binaryExpr.NodeType == ExpressionType.Add && binaryExpr.Type == typeof(string)
                && (binaryExpr.Left.Type == typeof(string) || binaryExpr.Right.Type == typeof(string)))
                return true;
            if (f is MethodCallExpression callExpr && callExpr.Method.Name == "Concat")
                return true;
            return false;
        };
        Expression nextExpr = null;
        for (int i = argsExprs.Length - 1; i > 0; i--)
        {
            deferredExprs.Push(argsExprs[i]);
        }
        nextExpr = argsExprs[0];
        while (true)
        {
            if (isConcatBinary(nextExpr))
            {
                //字符串连接+
                if (nextExpr is BinaryExpression binaryExpr)
                {
                    if (isConcatBinary(binaryExpr.Left))
                    {
                        deferredExprs.Push(binaryExpr.Right);
                        nextExpr = binaryExpr.Left;
                        continue;
                    }
                    completedExprs.Add(nextExpr);
                    if (isConcatBinary(binaryExpr.Right))
                    {
                        nextExpr = binaryExpr.Right;
                        continue;
                    }
                    completedExprs.Add(binaryExpr.Right);
                    if (!deferredExprs.TryPop(out nextExpr))
                        break;
                    continue;
                }
                else
                {
                    var callExpr = nextExpr as MethodCallExpression;
                    for (int i = callExpr.Arguments.Count - 1; i > 0; i--)
                    {
                        deferredExprs.Push(callExpr.Arguments[i]);
                    }
                    nextExpr = callExpr.Arguments[0];
                    continue;
                }
            }
            completedExprs.Add(nextExpr);
            if (!deferredExprs.TryPop(out nextExpr))
                break;
        }
        return completedExprs;
    }
    public virtual Expression[] SplitConcatList(Expression concatExpr)
    {
        var completedExprs = new List<Expression>();
        var deferredExprs = new Stack<Expression>();
        Func<Expression, bool> isConcatBinary = f =>
        {
            if (f is BinaryExpression binaryExpr && binaryExpr.NodeType == ExpressionType.Add && binaryExpr.Type == typeof(string)
                && (binaryExpr.Left.Type == typeof(string) || binaryExpr.Right.Type == typeof(string)))
                return true;
            if (f is MethodCallExpression callExpr && callExpr.Method.Name == "Concat")
                return true;
            return false;
        };
        var nextExpr = concatExpr;
        while (true)
        {
            if (isConcatBinary(nextExpr))
            {
                //字符串连接+
                if (nextExpr is BinaryExpression binaryExpr)
                {
                    if (isConcatBinary(binaryExpr.Left))
                    {
                        deferredExprs.Push(binaryExpr.Right);
                        nextExpr = binaryExpr.Left;
                        continue;
                    }
                    completedExprs.Add(binaryExpr.Left);
                    if (isConcatBinary(binaryExpr.Right))
                    {
                        nextExpr = binaryExpr.Right;
                        continue;
                    }
                    completedExprs.Add(binaryExpr.Right);
                    if (!deferredExprs.TryPop(out nextExpr))
                        break;
                    continue;
                }
                else
                {
                    //Concat方法
                    var callExpr = nextExpr as MethodCallExpression;
                    for (int i = callExpr.Arguments.Count - 1; i > 0; i--)
                    {
                        deferredExprs.Push(callExpr.Arguments[i]);
                    }
                    nextExpr = callExpr.Arguments[0];
                    continue;
                }
            }
            completedExprs.Add(nextExpr);
            if (!deferredExprs.TryPop(out nextExpr))
                break;
        }
        return completedExprs.ToArray();
    }
    public virtual List<ReaderField> AddTableRecursiveReaderFields(int readerIndex, TableSegment fromSegment)
    {
        var readerFields = new List<ReaderField>();
        fromSegment.Mapper ??= this.mapProvider.GetEntityMap(fromSegment.EntityType);
        var lastReaderField = new ReaderField
        {
            Index = readerIndex,
            FieldType = ReaderFieldType.Entity,
            TableSegment = fromSegment,
            ReaderFields = this.FlattenTableFields(fromSegment)
            //最外层Select对象的成员，位于顶层, FromMember暂时不设置值，到Select时候去设置 
        };
        readerFields.Add(lastReaderField);
        this.AddIncludeTables(lastReaderField, readerFields);
        return readerFields;
    }
    public virtual IDbDataParameter CreateParameter(MemberMap memberMapper, string parameterName, object fieldValue)
    {
        IDbDataParameter dbParameter = null;
        if (memberMapper.TypeHandler != null)
        {
            if (memberMapper.NativeDbType != null)
                dbParameter = this.OrmProvider.CreateParameter(parameterName, memberMapper.NativeDbType, fieldValue);
            else dbParameter = this.OrmProvider.CreateParameter(parameterName, fieldValue);
            memberMapper.TypeHandler.SetValue(this.OrmProvider, dbParameter, fieldValue);
        }
        else
        {
            if (memberMapper.NativeDbType != null)
            {
                var parameters = this.OrmProvider.ToFieldValue(memberMapper, fieldValue);
                dbParameter = this.OrmProvider.CreateParameter(parameterName, memberMapper.NativeDbType, parameters);
            }
            else dbParameter = this.OrmProvider.CreateParameter(parameterName, fieldValue);
        }
        return dbParameter;
    }
    public virtual string VisitFromQuery(LambdaExpression lambdaExpr, out bool isNeedAlias)
    {
        var currentExpr = lambdaExpr.Body;
        var callStack = new Stack<MethodCallExpression>();
        while (true)
        {
            if (currentExpr.NodeType == ExpressionType.Parameter)
                break;

            if (currentExpr is MethodCallExpression callExpr)
            {
                callStack.Push(callExpr);
                currentExpr = callExpr.Object;
            }
        }
        var queryVisitor = this.OrmProvider.NewQueryVisitor(this.dbKey, this.mapProvider, this.IsParameterized, TableAsStart, parameterPrefix);
        queryVisitor.IsNeedAlias = this.IsNeedAlias;
        while (callStack.TryPop(out var callExpr))
        {
            var genericArguments = callExpr.Method.GetGenericArguments();
            LambdaExpression lambdaArgsExpr = null;
            switch (callExpr.Method.Name)
            {
                case "From":
                case "Union":
                case "UnionAll":
                    queryVisitor.From(this.Evaluate<char>(callExpr.Arguments[0]), genericArguments);
                    break;
                case "InnerJoin":
                case "LeftJoin":
                case "RightJoin":
                    var joinType = callExpr.Method.Name switch
                    {
                        "LeftJoin" => "LEFT JOIN",
                        "RightJoin" => "RIGHT JOIN",
                        _ => "INNER JOIN"
                    };
                    queryVisitor.IsNeedAlias = true;
                    lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[0]);
                    if (lambdaArgsExpr.Body.GetParameters(out var visitedParameters))
                    {
                        foreach (var tableAlias in this.tableAlias.Keys)
                        {
                            if (visitedParameters.Exists(f => f.Name == tableAlias))
                                queryVisitor.AddTable(this.tableAlias[tableAlias]);
                        }
                        lambdaArgsExpr = Expression.Lambda(lambdaArgsExpr.Body, visitedParameters);
                    }
                    queryVisitor.Join(joinType, genericArguments[0], lambdaArgsExpr);
                    break;
                case "Where":
                    lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[0]);
                    queryVisitor.InitTableAlias(lambdaArgsExpr);
                    if (lambdaArgsExpr.Body.GetParameters(out visitedParameters))
                    {
                        queryVisitor.IsNeedAlias = true;
                        foreach (var tableAlias in this.tableAlias.Keys)
                        {
                            if (visitedParameters.Exists(f => f.Name == tableAlias))
                            {
                                var tableSegment = this.tableAlias[tableAlias];
                                queryVisitor.AddAliasTable(tableAlias, tableSegment);
                            }
                        }
                        lambdaArgsExpr = Expression.Lambda(lambdaArgsExpr.Body, visitedParameters);
                    }
                    queryVisitor.Where(lambdaArgsExpr, false);
                    break;
                case "And":
                    lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[1]);
                    if (this.Evaluate<bool>(callExpr.Arguments[0]))
                        queryVisitor.And(lambdaArgsExpr);
                    break;
                case "GroupBy":
                    lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[0]);
                    queryVisitor.GroupBy(lambdaArgsExpr);
                    break;
                case "Having":
                    if (callExpr.Arguments.Count > 1 && this.Evaluate<bool>(callExpr.Arguments[0]))
                    {
                        lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[1]);
                        queryVisitor.Having(lambdaArgsExpr);
                    }
                    else
                    {
                        lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[0]);
                        queryVisitor.Having(lambdaArgsExpr);
                    }
                    break;
                case "OrderBy":
                    lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[0]);
                    queryVisitor.OrderBy("ASC", lambdaArgsExpr);
                    break;
                case "OrderByDescending":
                    lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[0]);
                    queryVisitor.OrderBy("DESC", lambdaArgsExpr);
                    break;
                case "Select":
                case "SelectAggregate":
                    if (callExpr.Arguments[0].NodeType == ExpressionType.Constant)
                        queryVisitor.Select(this.Evaluate<string>(callExpr.Arguments[0]));
                    else
                    {
                        lambdaArgsExpr = this.EnsureLambda(callExpr.Arguments[0]);
                        queryVisitor.Select(null, lambdaArgsExpr);
                    }
                    break;
                case "Distinct":
                    queryVisitor.Distinct();
                    break;
            }
        }
        var result = queryVisitor.BuildSql(out var dbDataParameters, out _);
        if (dbDataParameters != null && dbDataParameters.Count > 0)
        {
            this.dbParameters ??= new();
            this.dbParameters.AddRange(dbDataParameters);
        }
        isNeedAlias = queryVisitor.IsNeedAlias;
        return result;
    }

    public virtual SqlSegment Merge(SqlSegment sqlSegment, SqlSegment rightSegment, object segmentValue)
    {
        sqlSegment.IsConstant = sqlSegment.IsConstant && rightSegment.IsConstant;
        sqlSegment.IsVariable = sqlSegment.IsVariable || rightSegment.IsVariable;
        sqlSegment.HasField = sqlSegment.HasField || rightSegment.HasField;
        sqlSegment.IsParameter = sqlSegment.IsParameter || rightSegment.IsParameter;
        return this.Change(sqlSegment, segmentValue);
    }
    public virtual SqlSegment Merge(SqlSegment sqlSegment, SqlSegment args0Segment, SqlSegment args1Segment, object segmentValue)
    {
        sqlSegment.IsConstant = sqlSegment.IsConstant && args0Segment.IsConstant && args1Segment.IsConstant;
        sqlSegment.IsVariable = sqlSegment.IsVariable || args0Segment.IsVariable || args1Segment.IsVariable;
        sqlSegment.HasField = sqlSegment.HasField || args0Segment.HasField || args1Segment.HasField;
        sqlSegment.IsParameter = sqlSegment.IsParameter || args0Segment.IsParameter || args1Segment.IsParameter;
        return this.Change(sqlSegment, segmentValue);
    }
    public virtual SqlSegment Merge(SqlSegment sqlSegment, SqlSegment rightSegment, object segmentValue, bool isExpression, bool isMethodCall)
    {
        sqlSegment.IsConstant = sqlSegment.IsConstant && rightSegment.IsConstant;
        sqlSegment.IsVariable = sqlSegment.IsVariable || rightSegment.IsVariable;
        sqlSegment.HasField = sqlSegment.HasField || rightSegment.HasField;
        sqlSegment.IsParameter = sqlSegment.IsParameter || rightSegment.IsParameter;
        return this.Change(sqlSegment, segmentValue, isExpression, isMethodCall);
    }
    public virtual SqlSegment Merge(SqlSegment sqlSegment, SqlSegment args0Segment, SqlSegment args1Segment, object segmentValue, bool isExpression, bool isMethodCall)
    {
        sqlSegment.IsConstant = sqlSegment.IsConstant && args0Segment.IsConstant && args1Segment.IsConstant;
        sqlSegment.IsVariable = sqlSegment.IsVariable || args0Segment.IsVariable || args1Segment.IsVariable;
        sqlSegment.HasField = sqlSegment.HasField || args0Segment.HasField || args1Segment.HasField;
        sqlSegment.IsParameter = sqlSegment.IsParameter || args0Segment.IsParameter || args1Segment.IsParameter;
        return this.Change(sqlSegment, segmentValue, isExpression, isMethodCall);
    }
    public virtual SqlSegment Change(SqlSegment sqlSegment)
    {
        if (sqlSegment.IsParameter) return sqlSegment;
        if (sqlSegment.IsVariable || (sqlSegment.IsParameterized || this.IsParameterized) && sqlSegment.IsConstant)
        {
            string parameterName = null;
            this.dbParameters ??= new();
            if (!string.IsNullOrEmpty(sqlSegment.ParameterName))
            {
                parameterName = this.OrmProvider.ParameterPrefix + sqlSegment.ParameterName;
                if (this.dbParameters.Exists(f => f.ParameterName == parameterName))
                    parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();
            }
            else parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();

            IDbDataParameter dbParameter = null;
            if (sqlSegment.MemberMapper != null)
                dbParameter = this.CreateParameter(sqlSegment.MemberMapper, parameterName, sqlSegment.Value);
            else dbParameter = this.OrmProvider.CreateParameter(parameterName, sqlSegment.Value);
            this.dbParameters.Add(dbParameter);
            sqlSegment.Value = parameterName;
            sqlSegment.IsParameter = true;
            sqlSegment.IsVariable = false;
            sqlSegment.IsConstant = false;
            return sqlSegment;
        }
        if (sqlSegment.IsConstant && sqlSegment.MemberMapper != null && sqlSegment.TargetType != sqlSegment.Type)
        {
            sqlSegment.Value = this.OrmProvider.ToFieldValue(sqlSegment.MemberMapper, sqlSegment.Value);
            sqlSegment.Type = sqlSegment.TargetType;
        }
        return sqlSegment;
    }
    public virtual SqlSegment Change(SqlSegment sqlSegment, object segmentValue)
    {
        sqlSegment.Value = segmentValue;
        return this.Change(sqlSegment);
    }
    public virtual SqlSegment Change(SqlSegment sqlSegment, object segmentValue, bool isExpression, bool isMethodCall)
    {
        sqlSegment.IsExpression = isExpression;
        sqlSegment.IsMethodCall = isMethodCall;
        if (sqlSegment.IsConstant && (isExpression || isMethodCall))
            sqlSegment.IsConstant = false;
        return this.Change(sqlSegment, segmentValue);
    }
    public virtual string GetQuotedValue(object fieldValue, MemberMap memberMapper = null, bool? isVariable = null, int? index = null, string nullValue = "NULL")
    {
        if (fieldValue is DBNull)
            return nullValue;
        if (fieldValue is SqlSegment sqlSegment)
        {
            if (sqlSegment == SqlSegment.Null)
                return nullValue;
            //默认只要是变量就设置为参数
            if (isVariable.HasValue && isVariable.Value || sqlSegment.IsVariable
                || (this.IsParameterized || sqlSegment.IsParameterized) && sqlSegment.IsConstant)
            {
                string parameterName = null;
                this.dbParameters ??= new();
                if (!string.IsNullOrEmpty(sqlSegment.ParameterName))
                {
                    parameterName = this.OrmProvider.ParameterPrefix + sqlSegment.ParameterName;
                    if (this.dbParameters.Exists(f => f.ParameterName == parameterName))
                        parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();
                }
                else parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();

                if (index.HasValue)
                    parameterName += index.ToString();
                var myMemberMapper = sqlSegment.MemberMapper ?? memberMapper;
                IDbDataParameter dbParameter = null;
                if (myMemberMapper != null)
                    dbParameter = this.CreateParameter(myMemberMapper, parameterName, sqlSegment.Value);
                else dbParameter = this.OrmProvider.CreateParameter(parameterName, sqlSegment.Value);
                this.dbParameters.Add(dbParameter);
                return parameterName;
            }
            else if (sqlSegment.IsConstant)
            {
                if (sqlSegment.MemberMapper != null && sqlSegment.TargetType != sqlSegment.Type)
                {
                    sqlSegment.Value = this.OrmProvider.ToFieldValue(sqlSegment.MemberMapper, sqlSegment.Value);
                    sqlSegment.Type = sqlSegment.TargetType;
                    return this.OrmProvider.GetQuotedValue(sqlSegment.Value);
                }
                return this.OrmProvider.GetQuotedValue(sqlSegment);
            }
            else return sqlSegment.ToString();
        }
        else
        {
            if (isVariable.HasValue && isVariable.Value || this.IsParameterized)
            {
                this.dbParameters ??= new();
                var parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();
                if (index.HasValue)
                    parameterName += index.ToString();
                IDbDataParameter dbParameter = null;
                if (memberMapper != null)
                    dbParameter = this.CreateParameter(memberMapper, parameterName, fieldValue);
                else dbParameter = this.OrmProvider.CreateParameter(parameterName, fieldValue);
                this.dbParameters.Add(dbParameter);
                return parameterName;
            }
            if (fieldValue == null) return nullValue;
            return this.OrmProvider.GetQuotedValue(fieldValue);
        }
    }
    public virtual string GetQuotedValue(SqlSegment sqlSegment, int? index = null, string nullValue = "NULL")
    {
        if (sqlSegment == SqlSegment.Null)
            return nullValue;
        //默认只要是变量就设置为参数
        if (sqlSegment.IsVariable || (this.IsParameterized || sqlSegment.IsParameterized) && sqlSegment.IsConstant)
        {
            string parameterName = null;
            this.dbParameters ??= new();
            if (!string.IsNullOrEmpty(sqlSegment.ParameterName))
            {
                parameterName = this.OrmProvider.ParameterPrefix + sqlSegment.ParameterName;
                if (this.dbParameters.Exists(f => f.ParameterName == parameterName))
                    parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();
            }
            else parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();

            if (index.HasValue)
                parameterName += index.ToString();
            IDbDataParameter dbParameter = null;
            if (sqlSegment.MemberMapper != null)
                dbParameter = this.CreateParameter(sqlSegment.MemberMapper, parameterName, sqlSegment.Value);
            else dbParameter = this.OrmProvider.CreateParameter(parameterName, sqlSegment.Value);
            this.dbParameters.Add(dbParameter);
            sqlSegment.IsConstant = false;
            sqlSegment.IsVariable = false;
            sqlSegment.IsParameter = true;
            return parameterName;
        }
        else if (sqlSegment.IsConstant)
        {
            if (sqlSegment.MemberMapper != null && sqlSegment.TargetType != sqlSegment.Type)
            {
                sqlSegment.Value = this.OrmProvider.ToFieldValue(sqlSegment.MemberMapper, sqlSegment.Value);
                sqlSegment.Type = sqlSegment.TargetType;
                return this.OrmProvider.GetQuotedValue(sqlSegment.Value);
            }
            return this.OrmProvider.GetQuotedValue(sqlSegment);
        }
        else return sqlSegment.ToString();
    }
    public virtual string GetQuotedValue(object elementValue, SqlSegment arraySegment, int? index = null, string nullValue = "NULL")
    {
        if (elementValue is DBNull || elementValue == null)
            return nullValue;
        if (arraySegment.IsVariable || (this.IsParameterized || arraySegment.IsParameterized) && arraySegment.IsConstant)
        {
            this.dbParameters ??= new();
            var parameterName = this.OrmProvider.ParameterPrefix + this.parameterPrefix + this.dbParameters.Count.ToString();
            if (index.HasValue)
                parameterName += index.ToString();
            IDbDataParameter dbParameter = null;
            if (arraySegment.MemberMapper != null)
                dbParameter = this.CreateParameter(arraySegment.MemberMapper, parameterName, elementValue);
            else dbParameter = this.OrmProvider.CreateParameter(parameterName, elementValue);
            this.dbParameters.Add(dbParameter);
            return parameterName;
        }
        if (arraySegment.IsConstant && arraySegment.MemberMapper != null && arraySegment.TargetType != arraySegment.Type)
            elementValue = this.OrmProvider.ToFieldValue(arraySegment.MemberMapper, elementValue);
        return this.OrmProvider.GetQuotedValue(elementValue);
    }
    public SqlSegment VisitDeferredBoolConditional(SqlSegment sqlSegment, bool isExpectBooleanType, string ifTrueValue, string ifFalseValue)
    {
        //处理HasValue !逻辑取反操作，这种情况下是一元操作
        int notIndex = 0;
        SqlSegment deferredSegment = null;
        while (sqlSegment.TryPop(out var deferredExpr))
        {
            switch (deferredExpr.OperationType)
            {
                case OperationType.Equal:
                    deferredSegment = deferredExpr.Value as SqlSegment;
                    break;
                case OperationType.Not:
                    notIndex++;
                    break;
            }
        }
        if (deferredSegment == null)
            deferredSegment = SqlSegment.True;

        string strOperator = null;
        if (notIndex % 2 > 0)
            strOperator = deferredSegment == SqlSegment.Null ? "IS NOT" : "<>";
        else strOperator = deferredSegment == SqlSegment.Null ? "IS" : "=";

        string strExpression = null;
        //TODO:测试一下true=true 或是1=0两个常量或是常量和变量的相等条件
        if ((this.isSelect && !sqlSegment.IsExpression) || (this.isWhere && !sqlSegment.IsExpression))
        {
            if (deferredSegment == SqlSegment.Null)
                strExpression = $"{sqlSegment} {strOperator} {this.GetQuotedValue(deferredSegment, sqlSegment.MemberMapper)}";
            else strExpression = $"{sqlSegment}{strOperator}{this.GetQuotedValue(deferredSegment, sqlSegment.MemberMapper)}";
        }
        else strExpression = $"{sqlSegment}";
        if (this.isSelect || (this.isWhere && !isExpectBooleanType))
            sqlSegment.Change($"CASE WHEN {strExpression} THEN {ifTrueValue} ELSE {ifFalseValue} END", false, true, false);
        else sqlSegment.Change($"{strExpression}", false, true, false);
        return sqlSegment;
    }
    public List<ReaderField> FlattenFieldsTo(Type targetType, Expression toTargetExpr = null)
    {
        List<ReaderField> targetFields = null;
        if (targetType == null)
            throw new ArgumentNullException(nameof(targetType));

        //通过表达式设置的字段
        if (toTargetExpr != null)
            targetFields = this.ConstructorFieldsTo(toTargetExpr as LambdaExpression);

        targetFields ??= new List<ReaderField>();
        var targetMembers = targetType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(f => f.MemberType == MemberTypes.Property | f.MemberType == MemberTypes.Field).ToList();

        for (int i = targetFields.Count - 1; i >= 0; i--)
        {
            var memberInfo = targetFields[i].TargetMember;
            if (!targetMembers.Exists(t => t.Name == memberInfo.Name))
                targetFields.RemoveAt(i);
        }
        foreach (var memberInfo in targetMembers)
        {
            var targetField = targetFields.Find(f => f.TargetMember.Name == memberInfo.Name);
            if (targetField != null)
            {
                targetField.TargetMember = memberInfo;
                continue;
            }
            if (this.FindReaderField(memberInfo, targetFields.Count, out var readerField))
                targetFields.Add(readerField);
        }
        return targetFields;
    }
    public List<ReaderField> ConstructorFieldsTo(LambdaExpression toTargetExpr)
    {
        List<ReaderField> result = null;
        if (toTargetExpr != null)
        {
            var sqlSegment = new SqlSegment { Expression = toTargetExpr.Body };
            switch (toTargetExpr.Body.NodeType)
            {
                case ExpressionType.MemberAccess:
                    sqlSegment = this.VisitMemberAccess(sqlSegment);
                    //成员访问，最多是Include子表访问或是临时表访问，如：x.Grouping分组，
                    //可能会有多个子表或是临时表
                    if (sqlSegment.MemberType == ReaderFieldType.Entity
                        || sqlSegment.MemberType == ReaderFieldType.AnonymousObject)
                        result = sqlSegment.Value as List<ReaderField>;
                    else
                    {
                        result = new List<ReaderField>
                        {
                            new ReaderField
                            {
                                TableSegment = sqlSegment.TableSegment,
                                FieldType = sqlSegment.MemberType,
                                FromMember = sqlSegment.FromMember,
                                TargetMember = sqlSegment.FromMember,
                                Body = sqlSegment.Value.ToString()
                            }
                        };
                    }
                    break;
                case ExpressionType.New:
                    sqlSegment = this.VisitNew(sqlSegment);
                    result = sqlSegment.Value as List<ReaderField>;
                    break;
                case ExpressionType.MemberInit:
                    sqlSegment = this.VisitMemberInit(sqlSegment);
                    result = sqlSegment.Value as List<ReaderField>;
                    break;
                case ExpressionType.Parameter:
                    sqlSegment = this.VisitParameter(sqlSegment);
                    result = sqlSegment.Value as List<ReaderField>;
                    break;
                default:
                    //单个字段或是方法调用
                    if (toTargetExpr.Body.NodeType == ExpressionType.Call)
                        sqlSegment.OriginalExpression = toTargetExpr;
                    sqlSegment = this.VisitAndDeferred(sqlSegment);
                    if (sqlSegment.Value is List<ReaderField> readerFields)
                        result = readerFields;
                    else
                    {
                        result = new List<ReaderField>
                        {
                            new ReaderField
                            {
                                TableSegment = sqlSegment.TableSegment,
                                FromMember = sqlSegment.FromMember,
                                Body = sqlSegment.Value.ToString()
                            }
                        };
                    }
                    break;
            }
        }
        return result;
    }
    /// <summary>
    /// 展开当前表tableSegment的所有字段，tableSegment是实体表(主表或Include表)
    /// repository.From(f => f.From<Order, OrderDetail, User>()
    ///         .Where((a, b, c) => a.Id == b.OrderId && a.BuyerId == c.Id)
    ///         .GroupBy((a, b, c) => new { OrderId = a.Id, a.BuyerId })
    ///         .Having((x, a, b, c) => c.Age > 20 && x.Sum(b.Amount) > 500)
    ///         .Select((x, a, b, c) => new { x.Grouping.OrderId, x.Grouping.BuyerId, TotalAmount = x.Sum(b.Amount) }))
    ///     .InnerJoin<User>((a, b) => a.BuyerId == b.Id)
    ///     .InnerJoin<Order>((a, b, c) => a.OrderId == c.Id)
    ///     .Select((a, b, c) => new { a.OrderId, a.BuyerId, Buyer = b, Order = c, a.TotalAmount })
    ///     .ToSql(out _);
    /// </summary>
    /// <param name="tableSegment"></param>
    /// <returns></returns>
    public List<ReaderField> FlattenTableFields(TableSegment tableSegment)
    {
        var targetFields = new List<ReaderField>();
        tableSegment.Mapper ??= this.mapProvider.GetEntityMap(tableSegment.EntityType);
        foreach (var memberMapper in tableSegment.Mapper.MemberMaps)
        {
            if (memberMapper.IsIgnore || memberMapper.IsNavigation
                || (memberMapper.MemberType.IsEntityType() && memberMapper.TypeHandler == null))
                continue;
            targetFields.Add(new ReaderField
            {
                TableSegment = tableSegment,
                FromMember = memberMapper.Member,
                TargetMember = memberMapper.Member,
                FieldType = ReaderFieldType.Field,
                Body = this.GetFieldName(tableSegment, memberMapper.FieldName)
            });
        }
        return targetFields;
    }
    /// <summary>
    /// 根据表、字段获取Field表达式,SELECT和WHERE语句都可以使用
    /// 有表且有别名，将生成a.Field，常量访问没有表则就是字段访问Field
    /// </summary>
    /// <param name="tableSegment">表</param> 
    /// <param name="fieldName">字段名称或是表达式</param>
    /// <returns></returns>
    public string GetFieldName(TableSegment tableSegment, string fieldName)
    {
        fieldName = this.OrmProvider.GetFieldName(fieldName);
        if (tableSegment != null && !string.IsNullOrEmpty(tableSegment.AliasName) && (this.IsNeedAlias || tableSegment.IsNeedAlais))
            fieldName = tableSegment.AliasName + "." + fieldName;
        return fieldName;
    }
    private List<ConditionExpression> VisitLogicBinaryExpr(Expression conditionExpr)
    {
        Func<Expression, bool> isConditionExpr = f => f.NodeType == ExpressionType.AndAlso || f.NodeType == ExpressionType.OrElse;

        int deep = 0;
        string lastOperationType = string.Empty;
        var operators = new Stack<ConditionOperator>();
        var leftExprs = new Stack<Expression>();
        var completedStackExprs = new Stack<ConditionExpression>();

        var nextExpr = conditionExpr as BinaryExpression;
        while (nextExpr != null)
        {
            var operationType = nextExpr.NodeType == ExpressionType.AndAlso ? " AND " : " OR ";
            if (!string.IsNullOrEmpty(lastOperationType) && lastOperationType != operationType)
                deep++;

            if (isConditionExpr(nextExpr.Right))
            {
                leftExprs.Push(nextExpr.Left);
                nextExpr = nextExpr.Right as BinaryExpression;
                lastOperationType = operationType;
                if (deep > 0)
                {
                    operators.Push(new ConditionOperator
                    {
                        OperatorType = operationType,
                        Deep = deep
                    });
                }
                continue;
            }
            //先压进右括号
            var lastDeep = 0;
            if (operators.TryPop(out var conditionOperator))
                lastDeep = conditionOperator.Deep;
            for (int i = deep; i > lastDeep; i--)
            {
                completedStackExprs.Push(new ConditionExpression
                {
                    ExpressionType = ConditionType.OperatorType,
                    Body = ")"
                });
            }
            //再压进右侧表达式
            completedStackExprs.Push(new ConditionExpression
            {
                ExpressionType = ConditionType.Expression,
                Body = nextExpr.Right
            });
            //再压进当前操作符
            completedStackExprs.Push(new ConditionExpression
            {
                ExpressionType = ConditionType.OperatorType,
                Body = operationType
            });
            if (isConditionExpr(nextExpr.Left))
            {
                nextExpr = nextExpr.Left as BinaryExpression;
                lastOperationType = operationType;
                if (deep > 0)
                {
                    operators.Push(new ConditionOperator
                    {
                        OperatorType = operationType,
                        Deep = deep
                    });
                }
                continue;
            }
            //再压进左侧表达式
            completedStackExprs.Push(new ConditionExpression
            {
                ExpressionType = ConditionType.Expression,
                Body = nextExpr.Left
            });
            if (operators.TryPop(out conditionOperator))
            {
                lastDeep = conditionOperator.Deep;
                lastOperationType = conditionOperator.OperatorType;
            }
            else lastDeep = 0;
            //再压进左括号
            for (int i = deep; i > lastDeep; i--)
            {
                completedStackExprs.Push(new ConditionExpression
                {
                    ExpressionType = ConditionType.OperatorType,
                    Body = "("
                });
            }
            //再压进操作符
            if (leftExprs.Count > 0)
            {
                for (int i = deep; i > lastDeep; i--)
                {
                    completedStackExprs.Push(new ConditionExpression
                    {
                        ExpressionType = ConditionType.OperatorType,
                        Body = lastOperationType
                    });
                }
            }
            if (leftExprs.TryPop(out var deferredExpr))
            {
                if (operators.TryPop(out conditionOperator))
                    deep = conditionOperator.Deep;
                else deep = 0;

                if (isConditionExpr(deferredExpr))
                {
                    nextExpr = deferredExpr as BinaryExpression;
                    continue;
                }
                completedStackExprs.Push(new ConditionExpression
                {
                    ExpressionType = ConditionType.Expression,
                    Body = deferredExpr
                });
                break;
            }
            else break;
        }
        var completedExprs = new List<ConditionExpression>();
        while (completedStackExprs.TryPop(out var completedExpr))
        {
            completedExprs.Add(completedExpr);
        }
        return completedExprs;
    }
    private bool FindReaderField(MemberInfo memberInfo, int index, out ReaderField readerField)
    {
        foreach (var tableSegment in this.tables)
        {
            if (this.FindReaderField(tableSegment, memberInfo, index, out readerField))
                return true;
        }
        readerField = null;
        return false;
    }
    private bool FindReaderField(TableSegment tableSegment, MemberInfo memberInfo, int index, out ReaderField readerField)
    {
        switch (tableSegment.TableType)
        {
            case TableType.FromQuery:
                if (tableSegment.ReaderFields == null || tableSegment.ReaderFields.Count == 0)
                {
                    readerField = null;
                    return false;
                }
                readerField = tableSegment.ReaderFields.Find(f => f.FromMember.Name == memberInfo.Name);
                if (readerField != null)
                {
                    readerField.Index = index;
                    readerField.TargetMember = memberInfo;
                    tableSegment.IsUsed = true;
                }
                return readerField != null;
            default:
                tableSegment.Mapper ??= this.mapProvider.GetEntityMap(tableSegment.EntityType);
                if (!tableSegment.Mapper.TryGetMemberMap(memberInfo.Name, out var memberMapper))
                {
                    readerField = null;
                    return false;
                }
                readerField = new ReaderField
                {
                    Index = index,
                    FieldType = ReaderFieldType.Field,
                    FromMember = memberMapper.Member,
                    TargetMember = memberInfo,
                    TableSegment = tableSegment,
                    Body = this.GetFieldName(tableSegment, memberMapper.FieldName)
                };
                tableSegment.IsUsed = true;
                return true;
        }
    }
    private bool IsDateTimeOperator(SqlSegment sqlSegment, out SqlSegment result)
    {
        var binaryExpr = sqlSegment.Expression as BinaryExpression;
        if (binaryExpr.Left.Type == typeof(DateTime) && binaryExpr.Right.Type == typeof(TimeSpan) && binaryExpr.NodeType == ExpressionType.Add)
        {
            var methodInfo = typeof(DateTime).GetMethod(nameof(DateTime.Add), new Type[] { binaryExpr.Right.Type });
            var operatorExpr = Expression.Call(binaryExpr.Left, methodInfo, binaryExpr.Right);
            result = this.VisitMethodCall(sqlSegment.Next(operatorExpr));
            return true;
        }
        if (binaryExpr.Left.Type == typeof(DateTime) && (binaryExpr.Right.Type == typeof(DateTime) || binaryExpr.Right.Type == typeof(TimeSpan)) && binaryExpr.NodeType == ExpressionType.Subtract)
        {
            var methodInfo = typeof(DateTime).GetMethod(nameof(DateTime.Subtract), new Type[] { binaryExpr.Right.Type });
            var operatorExpr = Expression.Call(binaryExpr.Left, methodInfo, binaryExpr.Right);
            result = this.VisitMethodCall(sqlSegment.Next(operatorExpr));
            return true;
        }
        result = null;
        return false;
    }
    private bool IsTimeSpanOperator(SqlSegment sqlSegment, out SqlSegment result)
    {
        var binaryExpr = sqlSegment.Expression as BinaryExpression;
        if (binaryExpr.Left.Type == typeof(TimeSpan) && binaryExpr.Right.Type == typeof(TimeSpan) && binaryExpr.NodeType == ExpressionType.Add)
        {
            var methodInfo = typeof(TimeSpan).GetMethod(nameof(TimeSpan.Add), new Type[] { binaryExpr.Right.Type });
            var operatorExpr = Expression.Call(binaryExpr.Left, methodInfo, binaryExpr.Right);
            result = this.VisitMethodCall(sqlSegment.Next(operatorExpr));
            return true;
        }
        if (binaryExpr.Left.Type == typeof(TimeSpan) && binaryExpr.Right.Type == typeof(TimeSpan) && binaryExpr.NodeType == ExpressionType.Subtract)
        {
            var methodInfo = typeof(TimeSpan).GetMethod(nameof(TimeSpan.Subtract), new Type[] { binaryExpr.Right.Type });
            var operatorExpr = Expression.Call(binaryExpr.Left, methodInfo, binaryExpr.Right);
            result = this.VisitMethodCall(sqlSegment.Next(operatorExpr));
            return true;
        }
        if (binaryExpr.Left.Type == typeof(TimeSpan) && binaryExpr.NodeType == ExpressionType.Multiply)
        {
            var rightExpr = binaryExpr.Right;
            if (binaryExpr.Right.Type != typeof(double))
                rightExpr = Expression.Convert(rightExpr, typeof(double));
            var methodInfo = typeof(TimeSpan).GetMethod(nameof(TimeSpan.Multiply), new Type[] { typeof(double) });
            var operatorExpr = Expression.Call(binaryExpr.Left, methodInfo, rightExpr);
            result = this.VisitMethodCall(sqlSegment.Next(operatorExpr));
            return true;
        }
        if (binaryExpr.Left.Type == typeof(TimeSpan) && binaryExpr.NodeType == ExpressionType.Divide)
        {
            Type divideType = null;
            if (binaryExpr.Right.Type == typeof(TimeSpan))
                divideType = typeof(TimeSpan);
            else divideType = typeof(double);
            var methodInfo = typeof(TimeSpan).GetMethod(nameof(TimeSpan.Divide), new Type[] { divideType });
            var rightExpr = binaryExpr.Right;
            if (divideType == typeof(double) && binaryExpr.Right.Type != typeof(double))
                rightExpr = Expression.Convert(rightExpr, typeof(double));
            var operatorExpr = Expression.Call(binaryExpr.Left, methodInfo, rightExpr);
            result = this.VisitMethodCall(sqlSegment.Next(operatorExpr));
            return true;
        }
        result = null;
        return false;
    }
    private SqlSegment CreateConditionSegment(Expression conditionExpr)
    {
        var sqlSegment = new SqlSegment { Expression = conditionExpr };
        if (conditionExpr.NodeType == ExpressionType.MemberAccess && conditionExpr.Type == typeof(bool))
        {
            sqlSegment.DeferredExprs ??= new();
            sqlSegment.DeferredExprs.Push(new DeferredExpr { OperationType = OperationType.Equal, Value = SqlSegment.True });
        }
        return sqlSegment;
    }
    private void Swap<T>(ref T left, ref T right)
    {
        var temp = right;
        right = left;
        left = temp;
    }
    private void AddIncludeTables(ReaderField lastReaderField, List<ReaderField> readerFields)
    {
        var includedSegments = this.tables.FindAll(f => !f.IsMaster && f.FromTable == lastReaderField.TableSegment);
        if (includedSegments != null && includedSegments.Count > 0)
        {
            lastReaderField.HasNextInclude = true;
            int index = lastReaderField.Index;
            foreach (var includedSegment in includedSegments)
            {
                index++;
                var readerField = new ReaderField
                {
                    Index = index,
                    FieldType = ReaderFieldType.Entity,
                    TableSegment = includedSegment,
                    FromMember = includedSegment.FromMember.Member,
                    TargetMember = includedSegment.FromMember.Member,
                    ParentIndex = lastReaderField.Index,
                    ReaderFields = this.FlattenTableFields(includedSegment)
                };
                //主表使用，include子表也将使用
                includedSegment.IsUsed = lastReaderField.TableSegment.IsUsed;
                readerFields.Add(readerField);
                if (this.tables.Exists(f => !f.IsMaster && f.FromTable == includedSegment))
                    this.AddIncludeTables(readerField, readerFields);
            }
        }
    }
    private LambdaExpression EnsureLambda(Expression expr)
    {
        if (expr.NodeType == ExpressionType.Lambda)
            return expr as LambdaExpression;
        var currentExpr = expr;
        while (true)
        {
            if (currentExpr.NodeType == ExpressionType.Lambda)
                break;

            if (currentExpr is UnaryExpression unaryExpr)
                currentExpr = unaryExpr.Operand;
        }
        return currentExpr as LambdaExpression;
    }
    class ConditionOperator
    {
        public string OperatorType { get; set; }
        public int Deep { get; set; }
    }
    class ConditionExpression
    {
        public object Body { get; set; }
        public ConditionType ExpressionType { get; set; }
    }
    enum ConditionType
    {
        OperatorType,
        Expression
    }
}
