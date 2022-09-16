using System;
using System.Text;

namespace Thea.Trolley;

class WhereBuilder
{
    private bool hasWhere = false;
    private StringBuilder sqlBuilder = new StringBuilder();
    public WhereBuilder RawSql(string clause)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" ");
        this.sqlBuilder.Append(clause);
        return this;
    }
    public WhereBuilder RawSql(bool condition, string clause)
    {
        if (condition)
        {
            if (String.IsNullOrEmpty(clause))
                throw new ArgumentNullException("clause", "clause不能为空字符串！");
            if (this.sqlBuilder.Length > 0)
                this.sqlBuilder.Append(" ");
            this.sqlBuilder.Append(clause);
        }
        return this;
    }
    public WhereBuilder RawSql(bool condition, string clause, string otherwise)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" ");
        if (condition) this.sqlBuilder.Append(clause);
        else this.sqlBuilder.Append(otherwise);
        return this;
    }
    public WhereBuilder Where(string clause)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" WHERE ");
        this.sqlBuilder.Append(clause);
        this.hasWhere = true;
        return this;
    }
    public WhereBuilder Where(bool condition, string clause)
    {
        if (condition)
        {
            if (String.IsNullOrEmpty(clause))
                throw new ArgumentNullException("clause", "clause不能为空字符串！");
            if (this.sqlBuilder.Length > 0)
                this.sqlBuilder.Append(" WHERE ");
            this.sqlBuilder.Append(clause);
            this.hasWhere = true;
        }
        return this;
    }
    public WhereBuilder Where(bool condition, string clause, string otherwise)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" WHERE ");

        if (condition) this.sqlBuilder.Append(clause);
        else this.sqlBuilder.Append(otherwise);
        this.hasWhere = true;
        return this;
    }
    public WhereBuilder And(string clause)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" AND ");
        this.sqlBuilder.Append(clause);
        return this;
    }
    public WhereBuilder And(bool condition, string clause)
    {
        if (condition)
        {
            if (String.IsNullOrEmpty(clause))
                throw new ArgumentNullException("clause", "clause不能为空字符串！");
            if (this.sqlBuilder.Length > 0)
                this.sqlBuilder.Append(" AND ");
            this.sqlBuilder.Append(clause);
        }
        return this;
    }
    public WhereBuilder And(bool condition, string clause, string otherwise)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" AND ");

        if (condition) this.sqlBuilder.Append(clause);
        else this.sqlBuilder.Append(otherwise);
        return this;
    }
    public WhereBuilder Or(string clause)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" OR ");
        this.sqlBuilder.Append(clause);
        return this;
    }
    public WhereBuilder Or(bool condition, string clause)
    {
        if (condition)
        {
            if (String.IsNullOrEmpty(clause))
                throw new ArgumentNullException("clause", "clause不能为空字符串！");
            if (this.sqlBuilder.Length > 0)
                this.sqlBuilder.Append(" OR ");
            this.sqlBuilder.Append(clause);
        }
        return this;
    }
    public WhereBuilder Or(bool condition, string clause, string otherwise)
    {
        if (String.IsNullOrEmpty(clause))
            throw new ArgumentNullException("clause", "clause不能为空字符串！");
        if (this.sqlBuilder.Length > 0)
            this.sqlBuilder.Append(" OR ");

        if (condition) this.sqlBuilder.Append(clause);
        else this.sqlBuilder.Append(otherwise);
        return this;
    }
    public string BuildSql()
    {
        var result = this.sqlBuilder.ToString().Trim();
        this.sqlBuilder = null;
        return result;
    }
}