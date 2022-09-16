using System;
using System.Diagnostics;
using System.Text;

namespace Thea
{
    [DebuggerDisplay("SQL:{BuildSql()}")]
    [DebuggerTypeProxy(typeof(SqlBuilder.DebugView))]
    public class SqlBuilder : ISqlBuilder
    {
        private StringBuilder sqlBuilder = new StringBuilder();
        public ISqlBuilder RawSql(string clause)
        {
            if (String.IsNullOrEmpty(clause))
                throw new ArgumentNullException("clause", "clause不能为空字符串！");
            if (this.sqlBuilder.Length > 0)
                this.sqlBuilder.Append(" ");
            this.sqlBuilder.Append(clause);
            return this;
        }
        public ISqlBuilder RawSql(bool condition, string clause)
        {
            if (condition) this.RawSql(clause);
            return this;
        }
        public ISqlBuilder RawSql(bool condition, string clause, string otherwise)
        {
            if (condition) this.RawSql(clause);
            else this.RawSql(otherwise);
            return this;
        }
        public ISqlBuilder Where(string clause)
        {
            this.sqlBuilder.Append(" WHERE " + clause);
            return this;
        }
        public ISqlBuilder Where(bool condition, string clause)
        {
            if (condition) this.sqlBuilder.Append(" WHERE " + clause);
            return this;
        }
        public ISqlBuilder Where(bool condition, string clause, string otherwise)
        {
            if (condition) this.sqlBuilder.Append(" WHERE " + clause);
            else this.sqlBuilder.Append(" WHERE " + otherwise);
            return this;
        }
        public ISqlBuilder And(string clause)
        {
            this.sqlBuilder.Append(" AND " + clause);
            return this;
        }
        public ISqlBuilder And(bool condition, string clause)
        {
            if (condition) this.sqlBuilder.Append(" AND " + clause);
            return this;
        }
        public ISqlBuilder And(bool condition, string clause, string otherwise)
        {
            if (condition) this.sqlBuilder.Append(" AND " + clause);
            else this.sqlBuilder.Append(" AND " + otherwise);
            return this;
        }
        public ISqlBuilder Or(string clause)
        {
            this.sqlBuilder.Append(" OR " + clause);
            return this;
        }
        public ISqlBuilder Or(bool condition, string clause)
        {
            if (condition) this.sqlBuilder.Append(" OR " + clause);
            return this;
        }
        public ISqlBuilder Or(bool condition, string clause, string otherwise)
        {
            if (condition) this.sqlBuilder.Append(" OR " + clause);
            else this.sqlBuilder.Append(" OR " + otherwise);
            return this;
        }
        public ISqlBuilder Where(Action<IClauseBuilder> clauseHandler)
        {
            var builder = new ClauseBuilder();
            clauseHandler(builder);
            var sql = builder.BuildSql();
            if (!string.IsNullOrEmpty(sql))
            {
                this.sqlBuilder.Append(" WHERE " + sql);
            }
            return this;
        }
        public ISqlBuilder And(Action<IClauseBuilder> clauseHandler)
        {
            var builder = new ClauseBuilder();
            clauseHandler(builder);
            var sql = builder.BuildSql();
            if (!string.IsNullOrEmpty(sql))
            {
                this.sqlBuilder.Append(" AND " + sql);
            }
            return this;
        }
        public ISqlBuilder Or(Action<IClauseBuilder> clauseHandler)
        {
            var builder = new ClauseBuilder();
            clauseHandler(builder);
            var sql = builder.BuildSql();
            if (!string.IsNullOrEmpty(sql))
            {
                this.sqlBuilder.Append(" OR " + sql);
            }
            return this;
        }
        public ISqlBuilder WhereWithParenthesis(Action<IClauseBuilder> clauseHandler)
        {
            var builder = new ClauseBuilder();
            clauseHandler(builder);
            var sql = builder.BuildSql();
            if (!string.IsNullOrEmpty(sql))
            {
                this.sqlBuilder.Append(" WHERE (" + sql + ")");
            }
            return this;
        }
        public ISqlBuilder AndWithParenthesis(Action<IClauseBuilder> clauseHandler)
        {
            var builder = new ClauseBuilder();
            clauseHandler(builder);
            var sql = builder.BuildSql();
            if (!string.IsNullOrEmpty(sql))
            {
                this.sqlBuilder.Append(" AND (" + sql + ")");
            }
            return this;
        }
        public ISqlBuilder OrWithParenthesis(Action<IClauseBuilder> clauseHandler)
        {
            var builder = new ClauseBuilder();
            clauseHandler(builder);
            var sql = builder.BuildSql();
            if (!string.IsNullOrEmpty(sql))
            {
                this.sqlBuilder.Append(" OR (" + sql + ")");
            }
            return this;
        }
        public string BuildSql()
        {
            var result = this.sqlBuilder.ToString();
            this.sqlBuilder.Clear();
            return result;
        }
        public void Clear() => this.sqlBuilder.Clear();
        public class ClauseBuilder : IClauseBuilder
        {
            private StringBuilder sqlBuilder = new StringBuilder();
            public IClauseBuilder RawSql(string clause)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" ");
                this.sqlBuilder.Append(clause);
                return this;
            }
            public IClauseBuilder RawSql(bool condition, string clause)
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
            public IClauseBuilder RawSql(bool condition, string clause, string otherwise)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" ");
                if (condition) this.sqlBuilder.Append(clause);
                else this.sqlBuilder.Append(otherwise);
                return this;
            }
            public IClauseBuilder Where(string clause)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" WHERE ");
                this.sqlBuilder.Append(clause);
                return this;
            }
            public IClauseBuilder Where(bool condition, string clause)
            {
                if (condition)
                {
                    if (String.IsNullOrEmpty(clause))
                        throw new ArgumentNullException("clause", "clause不能为空字符串！");
                    if (this.sqlBuilder.Length > 0)
                        this.sqlBuilder.Append(" WHERE ");
                    this.sqlBuilder.Append(clause);
                }
                return this;
            }
            public IClauseBuilder Where(bool condition, string clause, string otherwise)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" WHERE ");

                if (condition) this.sqlBuilder.Append(clause);
                else this.sqlBuilder.Append(otherwise);
                return this;
            }
            public IClauseBuilder And(string clause)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" AND ");
                this.sqlBuilder.Append(clause);
                return this;
            }
            public IClauseBuilder And(bool condition, string clause)
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
            public IClauseBuilder And(bool condition, string clause, string otherwise)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" AND ");

                if (condition) this.sqlBuilder.Append(clause);
                else this.sqlBuilder.Append(otherwise);
                return this;
            }
            public IClauseBuilder Or(string clause)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" OR ");
                this.sqlBuilder.Append(clause);
                return this;
            }
            public IClauseBuilder Or(bool condition, string clause)
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
            public IClauseBuilder Or(bool condition, string clause, string otherwise)
            {
                if (String.IsNullOrEmpty(clause))
                    throw new ArgumentNullException("clause", "clause不能为空字符串！");
                if (this.sqlBuilder.Length > 0)
                    this.sqlBuilder.Append(" OR ");

                if (condition) this.sqlBuilder.Append(clause);
                else this.sqlBuilder.Append(otherwise);
                return this;
            }
            public IClauseBuilder Where(Action<IClauseBuilder> clauseHandler)
            {
                if (clauseHandler == null)
                    throw new ArgumentNullException("clauseHandler", "clauseHandler不能为空！");

                var builder = new ClauseBuilder();
                clauseHandler(builder);
                var sql = builder.BuildSql();
                if (!string.IsNullOrEmpty(sql))
                {
                    if (this.sqlBuilder.Length > 0)
                        this.sqlBuilder.Append(" WHERE ");
                    this.sqlBuilder.Append(sql);
                }
                return this;
            }
            public IClauseBuilder WhereWithParenthesis(Action<IClauseBuilder> clauseHandler)
            {
                if (clauseHandler == null)
                    throw new ArgumentNullException("clauseHandler", "clauseHandler不能为空！");

                var builder = new ClauseBuilder();
                clauseHandler(builder);
                var sql = builder.BuildSql();
                if (!string.IsNullOrEmpty(sql))
                {
                    if (this.sqlBuilder.Length > 0)
                        this.sqlBuilder.Append(" WHERE (");
                    this.sqlBuilder.Append(sql + ")");
                }
                return this;
            }
            public IClauseBuilder And(Action<IClauseBuilder> clauseHandler)
            {
                if (clauseHandler == null)
                    throw new ArgumentNullException("clauseHandler", "clauseHandler不能为空！");

                var builder = new ClauseBuilder();
                clauseHandler(builder);
                var sql = builder.BuildSql();
                if (!string.IsNullOrEmpty(sql))
                {
                    if (this.sqlBuilder.Length > 0)
                        this.sqlBuilder.Append(" AND ");
                    this.sqlBuilder.Append(sql);
                }
                return this;
            }
            public IClauseBuilder AndWithParenthesis(Action<IClauseBuilder> clauseHandler)
            {
                if (clauseHandler == null)
                    throw new ArgumentNullException("clauseHandler", "clauseHandler不能为空！");

                var builder = new ClauseBuilder();
                clauseHandler(builder);
                var sql = builder.BuildSql();
                if (!string.IsNullOrEmpty(sql))
                {
                    if (this.sqlBuilder.Length > 0)
                        this.sqlBuilder.Append(" AND (");
                    this.sqlBuilder.Append(sql + ")");
                }
                return this;
            }
            public IClauseBuilder Or(Action<IClauseBuilder> clauseHandler)
            {
                if (clauseHandler == null)
                    throw new ArgumentNullException("clauseHandler", "clauseHandler不能为空！");

                var builder = new ClauseBuilder();
                clauseHandler(builder);
                var sql = builder.BuildSql();
                if (!string.IsNullOrEmpty(sql))
                {
                    if (this.sqlBuilder.Length > 0)
                        this.sqlBuilder.Append(" OR ");
                    this.sqlBuilder.Append(sql);
                }
                return this;
            }
            public IClauseBuilder OrWithParenthesis(Action<IClauseBuilder> clauseHandler)
            {
                if (clauseHandler == null)
                    throw new ArgumentNullException("clauseHandler", "clauseHandler不能为空！");

                var builder = new ClauseBuilder();
                clauseHandler(builder);
                var sql = builder.BuildSql();
                if (!string.IsNullOrEmpty(sql))
                {
                    if (this.sqlBuilder.Length > 0)
                        this.sqlBuilder.Append(" OR (");
                    this.sqlBuilder.Append(sql + ")");
                }
                return this;
            }
            public string BuildSql() => this.sqlBuilder.ToString().Trim();
        }
        sealed class DebugView
        {
            private readonly SqlBuilder builder;
            public DebugView(SqlBuilder builder) => this.builder = builder;
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public string SQL => this.builder.sqlBuilder.ToString();
        }
    }
}
