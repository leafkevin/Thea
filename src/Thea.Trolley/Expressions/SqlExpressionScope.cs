using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Thea.Orm;

namespace Thea.Trolley;

public enum SqlSegmentType : byte
{
    None = 0,
    From,
    InnerJoin,
    LeftJoin,
    RightJoin,
    Select,
    Distinct,
    Where,
    Take,
    Skip,
    Paging,
    OrderBy,
    OrderByDesc,
    //ThenBy,
    //ThenByDesc,
    GroupBy,
    //Include,
    //Aggregate 
}
class SqlSegment
{
    public static SqlSegment None = new SqlSegment { isFixValue = true, Value = string.Empty };
    public static SqlSegment Null = new SqlSegment { isFixValue = true, Value = "null" };
    public static SqlSegment True = new SqlSegment { isFixValue = true, Value = true };
    private bool isFixValue = false;
    public bool IsParameter { get; set; }
    public bool HasField { get; set; }
    public object Value { get; set; }
    public Expression Expression { get; set; }
    public override string ToString()
    {
        if (this.Value == null)
            return String.Empty;
        return this.Value.ToString();
    }
    protected bool Equals(SqlSegment other)
    {
        if (this.isFixValue != other.isFixValue)
            return false;
        return this.Value == other.Value;
    }
    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((SqlSegment)obj);
    }
    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = 17;
            hashCode = hashCode * 23 + this.isFixValue.GetHashCode();
            hashCode = hashCode * 23 + this.Value.GetHashCode();
            return hashCode;
        }
    }
}
/// <summary>
/// 针对当前操作数的延时表达式处理
/// </summary>
struct DeferredExpr
{
    public ExpressionType ExpressionType { get; set; }
    /// <summary>
    /// null/true/false常量或是MemberAccess成员表达式
    /// </summary>
    public object Value { get; set; }
}
class FromTableInfo
{
    public Type EntityType { get; set; }
    public EntityMap Mapper { get; set; }
    public string AlaisName { get; set; }
    public string JoinType { get; set; }
    public string JoinOn { get; set; }
}
class SqlExpressionScope
{
    /// <summary>
    /// Expression,SqlExpressionScope
    /// </summary>
    public object Value { get; set; }
    public string Separator { get; set; }
    public Expression Source { get; set; }
    /// <summary>
    /// Expression,SqlExpressionScope
    /// </summary>
    public Stack<object> NextExprs { get; set; }
    public SqlExpressionScope Parent { get; set; }
    public int Deep { get; set; }

    public SqlExpressionScope() { }
    public SqlExpressionScope(string separator) => this.Separator = separator;
    public void Push(object scopeExpr)
    {
        if (this.NextExprs == null)
            this.NextExprs = new Stack<object>();
        this.NextExprs.Push(scopeExpr);
        if (scopeExpr is SqlExpressionScope currentScope)
        {
            currentScope.Deep = this.Deep + 1;
            currentScope.Parent = this;
        }
    }
    public bool TryPop(out object scopeExpr)
    {
        if (this.NextExprs == null || this.NextExprs.Count == 0)
        {
            scopeExpr = null;
            return false;
        }
        return this.NextExprs.TryPop(out scopeExpr);
    }
}