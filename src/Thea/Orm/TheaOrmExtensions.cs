using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.Orm;

public static class TheaOrmExtensions
{
    public static void Configure(this IOrmDbFactory dbFactory, IModelConfiguration configuration)
        => configuration.OnModelCreating(new ModelBuilder(dbFactory));
    public static void Configure<TModelConfiguration>(this IOrmDbFactory dbFactory) where TModelConfiguration : class, IModelConfiguration, new()
        => new TModelConfiguration().OnModelCreating(new ModelBuilder(dbFactory));
    public static void Configure(this IOrmDbFactory dbFactory, Action<ModelBuilder> initializer)
    {
        if (initializer == null)
            throw new ArgumentNullException(nameof(initializer));

        initializer.Invoke(new ModelBuilder(dbFactory));
    }

    public static TEntity QueryFirst<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate = null)
        => repository.From<TEntity>().Where(predicate).First();
    public static async Task<TEntity> QueryFirstAsync<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await repository.From<TEntity>().Where(predicate).FirstAsync(cancellationToken);
    public static List<TEntity> Query<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate)
        => repository.From<TEntity>().Where(predicate).ToList();
    public static async Task<List<TEntity>> QueryAsync<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await repository.From<TEntity>().Where(predicate).ToListAsync(cancellationToken);
    public static IPagedList<TEntity> QueryPage<TEntity>(this IRepository repository, int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate = null)
        => repository.From<TEntity>().Where(predicate).ToPageList(pageIndex, pageSize);
    public static async Task<IPagedList<TEntity>> QueryPageAsync<TEntity>(this IRepository repository, int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate = null, CancellationToken cancellationToken = default)
        => await repository.From<TEntity>().Where(predicate).ToPageListAsync(pageIndex, pageSize, cancellationToken);

    public static int Create<TEntity>(this IRepository repository, object parameter)
        => repository.Create<TEntity>().WithBy(parameter).Execute();
    public static async Task<int> CreateAsync<TEntity>(this IRepository repository, object parameter, CancellationToken cancellationToken = default)
        => await repository.Create<TEntity>().WithBy(parameter).ExecuteAsync(cancellationToken);
    public static int Create<TEntity>(this IRepository repository, string sql, object parameter)
        => repository.Create<TEntity>().RawSql(sql, parameter).Execute();
    public static async Task<int> CreateAsync<TEntity>(this IRepository repository, string sql, object parameter, CancellationToken cancellationToken = default)
        => await repository.Create<TEntity>().RawSql(sql, parameter).ExecuteAsync(cancellationToken);
    public static int Create<TEntity>(this IRepository repository, IEnumerable entities, int bulkCount = 500)
        => repository.Create<TEntity>().WithBy(entities, bulkCount).Execute();
    public static async Task<int> CreateAsync<TEntity>(this IRepository repository, IEnumerable entities, int bulkCount = 500, CancellationToken cancellationToken = default)
        => await repository.Create<TEntity>().WithBy(entities, bulkCount).ExecuteAsync(cancellationToken);

    public static int Update<TEntity, TFields>(this IRepository repository, Expression<Func<TEntity, TFields>> fieldsExpr, Expression<Func<TEntity, bool>> predicate)
        => repository.Update<TEntity>().Set(fieldsExpr).Where(predicate).Execute();
    public static async Task<int> UpdateAsync<TEntity, TFields>(this IRepository repository, Expression<Func<TEntity, TFields>> fieldsExpr, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await repository.Update<TEntity>().Set(fieldsExpr).Where(predicate).ExecuteAsync(cancellationToken);
    public static int Update<TEntity, TField>(this IRepository repository, Expression<Func<TEntity, TField>> fieldExpr, object parameter)
        => repository.Update<TEntity>().WithBy(fieldExpr, parameter).Execute();
    public static async Task<int> UpdateAsync<TEntity, TField>(this IRepository repository, Expression<Func<TEntity, TField>> fieldExpr, object parameter, CancellationToken cancellationToken = default)
        => await repository.Update<TEntity>().WithBy(fieldExpr, parameter).ExecuteAsync(cancellationToken);
    public static int Update<TEntity, TFields>(this IRepository repository, Expression<Func<TEntity, TFields>> fieldsExpr, object parameters, int bulkCount = 500)
        => repository.Update<TEntity>().WithBy(fieldsExpr, parameters, bulkCount).Execute();
    public static async Task<int> UpdateAsync<TEntity, TFields>(this IRepository repository, Expression<Func<TEntity, TFields>> fieldsExpr, object parameters, int bulkCount = 500, CancellationToken cancellationToken = default)
        => await repository.Update<TEntity>().WithBy(fieldsExpr, parameters, bulkCount).ExecuteAsync(cancellationToken);

    public static int Delete<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate)
        => repository.Delete<TEntity>().Where(predicate).Execute();
    public static async Task<int> DeleteAsync<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await repository.Delete<TEntity>().Where(predicate).ExecuteAsync(cancellationToken);
    public static int Delete<TEntity>(this IRepository repository, object keys)
        => repository.Delete<TEntity>().Where(keys).Execute();
    public static async Task<int> DeleteAsync<TEntity>(this IRepository repository, object keys, CancellationToken cancellationToken = default)
        => await repository.Delete<TEntity>().Where(keys).ExecuteAsync(cancellationToken);

    public static bool IsEntityType(this Type type)
    {
        var typeCode = Type.GetTypeCode(type);
        switch (typeCode)
        {
            case TypeCode.DBNull:
            case TypeCode.Boolean:
            case TypeCode.Char:
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
            case TypeCode.DateTime:
            case TypeCode.String:
                return false;
        }
        if (type.IsClass) return true;
        if (type.IsValueType && !type.IsEnum && !type.IsPrimitive && type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Count(f => f.MemberType == MemberTypes.Field || (f.MemberType == MemberTypes.Property && f is PropertyInfo propertyInfo && propertyInfo.GetIndexParameters().Length == 0)) > 1)
            return true;
        return false;
    }
}
