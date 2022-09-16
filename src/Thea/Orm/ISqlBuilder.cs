using System;

namespace Thea.Orm;

public interface ISqlBuilder
{
    ISqlBuilder RawSql(string sql);
    ISqlBuilder RawSql(bool condition, string sql);
    ISqlBuilder RawSql(bool condition, string sql, string otherwise);
    ISqlBuilder Where(string clause);
    ISqlBuilder Where(bool condition, string clause);
    ISqlBuilder Where(bool condition, string clause, string otherwise);
    ISqlBuilder And(string clause);
    ISqlBuilder And(bool condition, string clause);
    ISqlBuilder And(bool condition, string clause, string otherwise);
    ISqlBuilder Or(string clause);
    ISqlBuilder Or(bool condition, string clause);
    ISqlBuilder Or(bool condition, string clause, string otherwise);
    ISqlBuilder Where(Action<IClauseBuilder> clauseHandler);
    ISqlBuilder And(Action<IClauseBuilder> clauseHandler);
    ISqlBuilder Or(Action<IClauseBuilder> clauseHandler);
    ISqlBuilder WhereWithParenthesis(Action<IClauseBuilder> clauseHandler);
    ISqlBuilder AndWithParenthesis(Action<IClauseBuilder> clauseHandler);
    ISqlBuilder OrWithParenthesis(Action<IClauseBuilder> clauseHandler);
    string BuildSql();
    void Clear();
}
public interface IClauseBuilder
{
    IClauseBuilder RawSql(string clause);
    IClauseBuilder RawSql(bool condition, string clause);
    IClauseBuilder RawSql(bool condition, string clause, string otherwise);
    IClauseBuilder Where(string clause);
    IClauseBuilder Where(bool condition, string clause);
    IClauseBuilder Where(bool condition, string clause, string otherwise);
    IClauseBuilder And(string clause);
    IClauseBuilder And(bool condition, string clause);
    IClauseBuilder And(bool condition, string clause, string otherwise);
    IClauseBuilder Or(string clause);
    IClauseBuilder Or(bool condition, string clause);
    IClauseBuilder Or(bool condition, string clause, string otherwise);
    IClauseBuilder Where(Action<IClauseBuilder> clauseHandler);
    IClauseBuilder And(Action<IClauseBuilder> clauseHandler);
    IClauseBuilder Or(Action<IClauseBuilder> clauseHandler);
    IClauseBuilder WhereWithParenthesis(Action<IClauseBuilder> clauseHandler);
    IClauseBuilder AndWithParenthesis(Action<IClauseBuilder> clauseHandler);
    IClauseBuilder OrWithParenthesis(Action<IClauseBuilder> clauseHandler);
    string BuildSql();
}
