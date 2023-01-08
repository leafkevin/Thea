using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.Orm;

public static class TheaOrmExtensions
{
    private static Type[] valueTypes = new Type[] {typeof(byte),typeof(sbyte),typeof(short),typeof(ushort),
        typeof(int),typeof(uint),typeof(long),typeof(ulong),typeof(float),typeof(double),typeof(decimal),
        typeof(bool),typeof(string),typeof(char),typeof(Guid),typeof(DateTime),typeof(DateTimeOffset),
        typeof(TimeSpan),typeof(byte[]),typeof(byte?),typeof(sbyte?),typeof(short?),typeof(ushort?),
        typeof(int?),typeof(uint?),typeof(long?),typeof(ulong?),typeof(float?),typeof(double?),typeof(decimal?),
        typeof(bool?),typeof(char?),typeof(Guid?) ,typeof(DateTime?),typeof(DateTimeOffset?),typeof(TimeSpan?) };

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
    public static async Task<TEntity> QueryFirstAsync<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate = null, CancellationToken cancellationToken = default)
        => await repository.From<TEntity>().Where(predicate).FirstAsync(cancellationToken);
    public static List<TEntity> Query<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate = null)
        => repository.From<TEntity>().Where(predicate).ToList();
    public static async Task<List<TEntity>> QueryAsync<TEntity>(this IRepository repository, Expression<Func<TEntity, bool>> predicate = null, CancellationToken cancellationToken = default)
        => await repository.From<TEntity>().Where(predicate).ToListAsync(cancellationToken);
    public static IPagedList<TEntity> QueryPage<TEntity>(this IRepository repository, int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate = null)
        => repository.From<TEntity>().Where(predicate).ToPageList(pageIndex, pageSize);
    public static async Task<IPagedList<TEntity>> QueryPageAsync<TEntity>(this IRepository repository, int pageIndex, int pageSize, Expression<Func<TEntity, bool>> predicate = null, CancellationToken cancellationToken = default)
        => await repository.From<TEntity>().Where(predicate).ToPageListAsync(pageIndex, pageSize, cancellationToken);
    public static Dictionary<TKey, TValue> QueryDictionary<TEntity, TKey, TValue>(this IRepository repository, Expression<Func<TEntity, bool>> predicate, Func<TEntity, TKey> keySelector, Func<TEntity, TValue> valueSelector) where TKey : notnull
        => repository.From<TEntity>().Where(predicate).ToDictionary(keySelector, valueSelector);
    public static async Task<Dictionary<TKey, TValue>> QueryDictionaryAsync<TEntity, TKey, TValue>(this IRepository repository, Expression<Func<TEntity, bool>> predicate, Func<TEntity, TKey> keySelector, Func<TEntity, TValue> valueSelector, CancellationToken cancellationToken = default) where TKey : notnull
        => await repository.From<TEntity>().Where(predicate).ToDictionaryAsync(keySelector, valueSelector, cancellationToken);


    public static int Create<TEntity>(this IRepository repository, object parameter)
        => repository.Create<TEntity>().WithBy(parameter).Execute();
    public static async Task<int> CreateAsync<TEntity>(this IRepository repository, object parameter, CancellationToken cancellationToken = default)
        => await repository.Create<TEntity>().WithBy(parameter).ExecuteAsync(cancellationToken);
    public static int Create<TEntity>(this IRepository repository, string rawSql, object parameter)
        => repository.Create<TEntity>().RawSql(rawSql, parameter).Execute();
    public static async Task<int> CreateAsync<TEntity>(this IRepository repository, string sql, object parameter, CancellationToken cancellationToken = default)
        => await repository.Create<TEntity>().RawSql(sql, parameter).ExecuteAsync(cancellationToken);
    public static int Create<TEntity>(this IRepository repository, IEnumerable entities, int bulkCount = 500)
        => repository.Create<TEntity>().WithBy(entities, bulkCount).Execute();
    public static async Task<int> CreateAsync<TEntity>(this IRepository repository, IEnumerable entities, int bulkCount = 500, CancellationToken cancellationToken = default)
        => await repository.Create<TEntity>().WithBy(entities, bulkCount).ExecuteAsync(cancellationToken);


    public static int Update<TEntity>(this IRepository repository, Expression<Func<TEntity, object>> fieldsExpr, Expression<Func<TEntity, bool>> predicate)
        => repository.Update<TEntity>().Set(fieldsExpr).Where(predicate).Execute();
    public static async Task<int> UpdateAsync<TEntity>(this IRepository repository, Expression<Func<TEntity, object>> fieldsExpr, Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await repository.Update<TEntity>().Set(fieldsExpr).Where(predicate).ExecuteAsync(cancellationToken);
    public static int Update<TEntity>(this IRepository repository, Expression<Func<TEntity, object>> fieldsExpr, object parameters, int bulkCount = 500)
        => repository.Update<TEntity>().WithBy(fieldsExpr, parameters, bulkCount).Execute();
    public static async Task<int> UpdateAsync<TEntity>(this IRepository repository, Expression<Func<TEntity, object>> fieldsExpr, object parameters, int bulkCount = 500, CancellationToken cancellationToken = default)
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
        if (valueTypes.Contains(type) || type.IsEnum) return false;
        if (type.FullName == "System.Data.Linq.Binary")
            return false;
        if (type.IsValueType)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null && underlyingType.IsEnum)
                return false;
        }
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            if (valueTypes.Contains(elementType) || elementType.IsEnum) return false;
            if (elementType.IsValueType)
            {
                var underlyingType = Nullable.GetUnderlyingType(elementType);
                if (underlyingType != null && underlyingType.IsEnum)
                    return false;
            }
        }
        return true;
    }
}
