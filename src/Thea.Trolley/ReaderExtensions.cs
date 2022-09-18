using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Thea.Orm;

namespace Thea.Trolley;

static class ReaderExtensions
{
    private static readonly ConcurrentDictionary<int, Func<IDataReader, object>> readerCache = new();
    public static TEntity To<TEntity>(this IDataReader reader, IOrmDbFactory dbFactory, TheaConnection connection)
    {
        var entityType = typeof(TEntity);
        var entityMapper = dbFactory.GetEntityMap(entityType);
        var func = GetReader(connection, reader, entityMapper);
        return (TEntity)func.Invoke(reader);
    }
    public static TEntity To<TEntity>(this IDataReader reader, TheaConnection connection, List<ReaderFieldInfo> readerFields)
    {
        var entityType = typeof(TEntity);
        var func = GetReader(connection, reader, entityType, readerFields);
        return (TEntity)func.Invoke(reader);
    }
    private static Func<IDataReader, object> GetReader(TheaConnection connection, IDataReader reader, EntityMap entityMapper)
    {
        var cacheKey = GetReaderKey(entityMapper.EntityType, connection, reader);
        if (!readerCache.TryGetValue(cacheKey, out var readerFunc))
        {
            var blockParameters = new List<ParameterExpression>();
            var blockBodies = new List<Expression>();
            var readerExpr = Expression.Parameter(typeof(IDataReader), "reader");
            var resultLabelExpr = Expression.Label(typeof(object));
            Expression returnExpr = null;

            var ctor = entityMapper.EntityType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor != null)
            {
                var entityExpr = Expression.Variable(entityMapper.EntityType, "entity");
                blockParameters.Add(entityExpr);
                blockBodies.Add(Expression.Assign(entityExpr, Expression.New(ctor)));

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var fieldName = reader.GetName(i);
                    var memberMapper = entityMapper.GetMemberMap(fieldName);
                    if (memberMapper.IsIgnore || (memberMapper.Member is PropertyInfo propertyInfo && propertyInfo.SetMethod == null))
                        continue;

                    var valueExpr = GetReaderValue(reader, readerExpr, i, fieldName, memberMapper, blockParameters, blockBodies);
                    blockBodies.Add(Expression.Assign(Expression.PropertyOrField(entityExpr, memberMapper.MemberName), valueExpr));
                }
                returnExpr = entityExpr;
            }
            else
            {
                ctor = entityMapper.EntityType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.IsPublic ? 0 : (f.IsPrivate ? 2 : 1)).First();
                var valuesExprs = new List<Expression>();
                var ctorParameters = ctor.GetParameters();

                for (int i = 0; i < ctorParameters.Length; i++)
                {
                    var fieldName = reader.GetName(i);
                    var memberMapper = entityMapper.GetMemberMap(fieldName);
                    if (memberMapper.IsIgnore || (memberMapper.Member is PropertyInfo propertyInfo && propertyInfo.SetMethod == null))
                        continue;
                    var valueExpr = GetReaderValue(reader, readerExpr, i, fieldName, memberMapper, blockParameters, blockBodies);
                    valuesExprs.Add(valueExpr);
                }
                returnExpr = Expression.New(ctor, valuesExprs);
            }

            blockBodies.Add(Expression.Return(resultLabelExpr, Expression.Convert(returnExpr, typeof(object))));
            blockBodies.Add(Expression.Label(resultLabelExpr, Expression.Constant(null, typeof(object))));

            readerFunc = Expression.Lambda<Func<IDataReader, object>>(Expression.Block(blockParameters, blockBodies), readerExpr).Compile();
            readerCache.TryAdd(cacheKey, readerFunc);
        }
        return readerFunc;
    }
    private static Func<IDataReader, object> GetReader(TheaConnection connection, IDataReader reader, Type entityType, List<ReaderFieldInfo> readerFields)
    {
        var cacheKey = GetReaderKey(entityType, connection, reader);
        if (!readerCache.TryGetValue(cacheKey, out var readerFunc))
        {
            var blockParameters = new List<ParameterExpression>();
            var blockBodies = new List<Expression>();
            var readerExpr = Expression.Parameter(typeof(IDataReader), "reader");
            var resultLabelExpr = Expression.Label(typeof(object));
            Expression returnExpr = null;

            var ctor = entityType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (ctor != null)
            {
                var entityExpr = Expression.Variable(entityType, "entity");
                blockParameters.Add(entityExpr);
                blockBodies.Add(Expression.Assign(entityExpr, Expression.New(ctor)));

                Type lastEntityType = null;
                int i = 0;
                while (i < reader.FieldCount)
                {
                    var fieldName = reader.GetName(i);
                    var readerFieldInfo = readerFields[i];
                    if (readerFieldInfo.Member is PropertyInfo propertyInfo && propertyInfo.SetMethod == null)
                    {
                        i++;
                        continue;
                    }

                    if (readerFieldInfo.IsTarget)
                    {
                        var valueExpr = GetReaderValue(reader, readerExpr, i, fieldName, readerFieldInfo, blockParameters, blockBodies);
                        blockBodies.Add(Expression.Assign(Expression.PropertyOrField(entityExpr, readerFieldInfo.MemberName), valueExpr));
                        i++;
                    }
                    else
                    {
                        var currentType = readerFieldInfo.Member.DeclaringType;
                        if (currentType != lastEntityType)
                        {
                            int nextIndex = i;
                            lastEntityType = currentType;
                            var newEntityExpr = NewInitEntity(reader, readerExpr, currentType, i, ref nextIndex, readerFields, blockParameters, blockBodies);
                            blockBodies.Add(Expression.Assign(Expression.PropertyOrField(entityExpr, readerFieldInfo.Member.Name), newEntityExpr));
                            break;
                        }
                    }
                }
                returnExpr = entityExpr;
            }
            else
            {
                ctor = entityType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.IsPublic ? 0 : (f.IsPrivate ? 2 : 1)).First();
                var valuesExprs = new List<Expression>();
                var ctorParameters = ctor.GetParameters();

                int i = 0;
                Type lastEntityType = null;
                var instanceExprs = new Dictionary<string, Expression>();
                while (i < reader.FieldCount)
                {
                    var fieldName = reader.GetName(i);
                    var readerFieldInfo = readerFields[i];
                    if (readerFieldInfo.IsTarget)
                    {
                        var valueExpr = GetReaderValue(reader, readerExpr, i, fieldName, readerFieldInfo, blockParameters, blockBodies);
                        valuesExprs.Add(valueExpr);
                        i++;
                        continue;
                    }

                    var currentType = readerFieldInfo.Member.DeclaringType;
                    if (!instanceExprs.TryGetValue(readerFieldInfo.Member.Name, out var instanceExpr))
                    {
                        int nextIndex = i;
                        lastEntityType = currentType;
                        instanceExpr = NewInitEntity(reader, readerExpr, currentType, i, ref i, readerFields, blockParameters, blockBodies);
                        valuesExprs.Add(instanceExpr);
                        instanceExprs.Add(readerFieldInfo.Member.Name, instanceExpr);
                        i = nextIndex;
                    }
                }
                returnExpr = Expression.New(ctor, valuesExprs);
            }

            blockBodies.Add(Expression.Return(resultLabelExpr, Expression.Convert(returnExpr, typeof(object))));
            blockBodies.Add(Expression.Label(resultLabelExpr, Expression.Constant(null, typeof(object))));

            readerFunc = Expression.Lambda<Func<IDataReader, object>>(Expression.Block(blockParameters, blockBodies), readerExpr).Compile();
            readerCache.TryAdd(cacheKey, readerFunc);
        }
        return readerFunc;
    }
    private static Expression GetReaderValue(IDataReader reader, ParameterExpression readerExpr, int index, string fieldName,
        MemberMap memberMapper, List<ParameterExpression> blockParameters, List<Expression> blockBodies)
    {
        MethodInfo methodInfo = null;
        Expression typedValueExpr = null;

        var readerType = reader.GetFieldType(index);
        //reader.GetValue(index);
        methodInfo = typeof(IDataRecord).GetMethod("GetValue", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(int) });
        var readerValueExpr = Expression.Call(readerExpr, methodInfo, Expression.Constant(index, typeof(int)));

        //null或default(int)
        Expression defaultValueExpr = Expression.Default(memberMapper.MemberType);

        if (memberMapper.MemberType.IsAssignableFrom(readerType))
            typedValueExpr = Expression.Convert(readerValueExpr, memberMapper.MemberType);
        else if (memberMapper.UnderlyingType == typeof(Guid))
        {
            if (readerType != typeof(string) && readerType != typeof(byte[]))
                throw new Exception($"数据库字段{fieldName}，无法初始化{memberMapper.Parent.EntityType.FullName}类型的Guid类型{memberMapper.MemberName}成员");

            typedValueExpr = Expression.New(typeof(Guid).GetConstructor(new Type[] { readerType }), Expression.Convert(readerValueExpr, readerType));
            if (!memberMapper.IsNullable) defaultValueExpr = Expression.Constant(Guid.Empty, typeof(Guid));
        }
        else
        {
            //else propValue=Convert.ToInt32(reader[index]);
            var typeCode = Type.GetTypeCode(memberMapper.UnderlyingType);
            string toTypeMethod = null;
            switch (typeCode)
            {
                case TypeCode.Boolean: toTypeMethod = nameof(Convert.ToBoolean); break;
                case TypeCode.Char: toTypeMethod = nameof(Convert.ToChar); break;
                case TypeCode.Byte: toTypeMethod = nameof(Convert.ToByte); break;
                case TypeCode.SByte: toTypeMethod = nameof(Convert.ToSByte); break;
                case TypeCode.Int16: toTypeMethod = nameof(Convert.ToInt16); break;
                case TypeCode.UInt16: toTypeMethod = nameof(Convert.ToUInt16); break;
                case TypeCode.Int32: toTypeMethod = nameof(Convert.ToInt32); break;
                case TypeCode.UInt32: toTypeMethod = nameof(Convert.ToUInt32); break;
                case TypeCode.Int64: toTypeMethod = nameof(Convert.ToInt64); break;
                case TypeCode.UInt64: toTypeMethod = nameof(Convert.ToUInt64); break;
                case TypeCode.Single: toTypeMethod = nameof(Convert.ToSingle); break;
                case TypeCode.Double: toTypeMethod = nameof(Convert.ToDouble); break;
                case TypeCode.Decimal: toTypeMethod = nameof(Convert.ToDecimal); break;
                case TypeCode.DateTime: toTypeMethod = nameof(Convert.ToDateTime); break;
                case TypeCode.String: toTypeMethod = nameof(Convert.ToString); break;
            }

            methodInfo = typeof(Convert).GetMethod(toTypeMethod, new Type[] { typeof(object), typeof(IFormatProvider) });
            typedValueExpr = Expression.Call(methodInfo, readerValueExpr, Expression.Constant(CultureInfo.CurrentCulture));
            if (memberMapper.IsEnum)
            {
                methodInfo = typeof(Enum).GetMethod(nameof(Enum.ToObject), new Type[] { typeof(Type), memberMapper.UnderlyingType });
                var toEnumExpr = Expression.Call(methodInfo, Expression.Constant(memberMapper.EnumUnderlyingType), typedValueExpr);
                typedValueExpr = Expression.Convert(toEnumExpr, memberMapper.EnumUnderlyingType);
            }
            if (memberMapper.IsNullable) typedValueExpr = Expression.Convert(typedValueExpr, memberMapper.MemberType);
        }

        //if(localValue is DBNull)
        var isNullExpr = Expression.TypeIs(readerValueExpr, typeof(DBNull));
        return Expression.Condition(isNullExpr, defaultValueExpr, typedValueExpr);
    }
    private static Expression GetReaderValue(IDataReader reader, ParameterExpression readerExpr, int index, string fieldName,
        ReaderFieldInfo readerFieldInfo, List<ParameterExpression> blockParameters, List<Expression> blockBodies)
    {
        MethodInfo methodInfo = null;
        Expression typedValueExpr = null;

        var readerType = reader.GetFieldType(index);
        //reader.GetValue(index);
        methodInfo = typeof(IDataRecord).GetMethod("GetValue", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(int) });
        var readerValueExpr = Expression.Call(readerExpr, methodInfo, Expression.Constant(index, typeof(int)));

        //null或default(int)
        var entityType = readerFieldInfo.Member.DeclaringType;
        Type memberType = null;
        if (readerFieldInfo.Member is PropertyInfo propertyInfo)
            memberType = propertyInfo.PropertyType;
        else if (readerFieldInfo.Member is FieldInfo fieldInfo)
            memberType = fieldInfo.FieldType;

        var underlyingType = Nullable.GetUnderlyingType(memberType);
        bool isNullable = underlyingType != null;
        if (!isNullable) underlyingType = memberType;

        Expression defaultValueExpr = Expression.Default(memberType);

        if (memberType.IsAssignableFrom(readerType))
            typedValueExpr = Expression.Convert(readerValueExpr, memberType);
        else if (underlyingType == typeof(Guid))
        {
            if (readerType != typeof(string) && readerType != typeof(byte[]))
                throw new Exception($"数据库字段{fieldName}，无法初始化{entityType.FullName}类型的Guid类型{readerFieldInfo.Member.Name}成员");

            typedValueExpr = Expression.New(typeof(Guid).GetConstructor(new Type[] { readerType }), Expression.Convert(readerValueExpr, readerType));
            if (!isNullable) defaultValueExpr = Expression.Constant(Guid.Empty, typeof(Guid));
        }
        else
        {
            //else propValue=Convert.ToInt32(reader[index]);
            var typeCode = Type.GetTypeCode(underlyingType);
            string toTypeMethod = null;
            switch (typeCode)
            {
                case TypeCode.Boolean: toTypeMethod = nameof(Convert.ToBoolean); break;
                case TypeCode.Char: toTypeMethod = nameof(Convert.ToChar); break;
                case TypeCode.Byte: toTypeMethod = nameof(Convert.ToByte); break;
                case TypeCode.SByte: toTypeMethod = nameof(Convert.ToSByte); break;
                case TypeCode.Int16: toTypeMethod = nameof(Convert.ToInt16); break;
                case TypeCode.UInt16: toTypeMethod = nameof(Convert.ToUInt16); break;
                case TypeCode.Int32: toTypeMethod = nameof(Convert.ToInt32); break;
                case TypeCode.UInt32: toTypeMethod = nameof(Convert.ToUInt32); break;
                case TypeCode.Int64: toTypeMethod = nameof(Convert.ToInt64); break;
                case TypeCode.UInt64: toTypeMethod = nameof(Convert.ToUInt64); break;
                case TypeCode.Single: toTypeMethod = nameof(Convert.ToSingle); break;
                case TypeCode.Double: toTypeMethod = nameof(Convert.ToDouble); break;
                case TypeCode.Decimal: toTypeMethod = nameof(Convert.ToDecimal); break;
                case TypeCode.DateTime: toTypeMethod = nameof(Convert.ToDateTime); break;
                case TypeCode.String: toTypeMethod = nameof(Convert.ToString); break;
            }

            methodInfo = typeof(Convert).GetMethod(toTypeMethod, new Type[] { typeof(object), typeof(IFormatProvider) });
            typedValueExpr = Expression.Call(methodInfo, readerValueExpr, Expression.Constant(CultureInfo.CurrentCulture));
            if (underlyingType.IsEnum)
            {
                var enumUnderlyingType = underlyingType.GetEnumUnderlyingType();
                methodInfo = typeof(Enum).GetMethod(nameof(Enum.ToObject), new Type[] { typeof(Type), underlyingType });
                var toEnumExpr = Expression.Call(methodInfo, Expression.Constant(enumUnderlyingType), typedValueExpr);
                typedValueExpr = Expression.Convert(toEnumExpr, enumUnderlyingType);
            }
            if (isNullable) typedValueExpr = Expression.Convert(typedValueExpr, memberType);
        }

        //if(localValue is DBNull)
        var isNullExpr = Expression.TypeIs(readerValueExpr, typeof(DBNull));
        return Expression.Condition(isNullExpr, defaultValueExpr, typedValueExpr);
    }
    private static Expression NewInitEntity(IDataReader reader, ParameterExpression readerExpr, Type entityType, int startIndex,
        ref int nextIndex, List<ReaderFieldInfo> readerFields, List<ParameterExpression> blockParameters, List<Expression> blockBodies)
    {
        Expression returnExpr = null;
        var ctor = entityType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (ctor != null)
        {
            var entityExpr = Expression.Variable(entityType, $"entity{startIndex}");
            blockParameters.Add(entityExpr);
            blockBodies.Add(Expression.Assign(entityExpr, Expression.New(ctor)));

            var currentEntityType = entityType;
            for (int i = startIndex; i < reader.FieldCount; i++)
            {
                var fieldName = reader.GetName(i);
                var readerFieldInfo = readerFields[i];
                if (readerFieldInfo.Member.DeclaringType != entityType)
                {
                    nextIndex = i;
                    break;
                }
                if (readerFieldInfo.Member is PropertyInfo propertyInfo && propertyInfo.SetMethod == null)
                    continue;

                var valueExpr = GetReaderValue(reader, readerExpr, i, fieldName, readerFieldInfo, blockParameters, blockBodies);
                blockBodies.Add(Expression.Assign(Expression.PropertyOrField(entityExpr, readerFieldInfo.MemberName), valueExpr));
            }
            returnExpr = entityExpr;
        }
        else
        {
            ctor = entityType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(f => f.IsPublic ? 0 : (f.IsPrivate ? 2 : 1)).First();
            var valuesExprs = new List<Expression>();
            var ctorParameters = ctor.GetParameters();

            int index = 0;
            for (int i = startIndex; i < reader.FieldCount; i++)
            {
                if (index == ctorParameters.Length)
                {
                    nextIndex = i;
                    break;
                }
                var fieldName = reader.GetName(i);
                var readerFieldInfo = readerFields[i];
                var valueExpr = GetReaderValue(reader, readerExpr, i, fieldName, readerFieldInfo, blockParameters, blockBodies);
                valuesExprs.Add(valueExpr);
                index++;
            }
            returnExpr = Expression.New(ctor, valuesExprs);
        }
        return returnExpr;
    }
    private static int GetReaderKey(Type entityType, TheaConnection connection, IDataReader reader)
    {
        var hashCode = new HashCode();
        hashCode.Add(entityType);
        hashCode.Add(connection);
        hashCode.Add(connection.OrmProvider);
        hashCode.Add(reader.FieldCount);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            hashCode.Add(reader.GetName(i));
        }
        return hashCode.ToHashCode();
    }
}
