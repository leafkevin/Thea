﻿using System;
using System.Linq.Expressions;
using Thea.Orm;

namespace Thea.Trolley.SqlServer;

partial class SqlServerProvider
{
    public override bool TryGetConvertMethodCallSqlFormatter(MethodCallExpression methodCallExpr, out MethodCallSqlFormatter formatter)
    {
        var result = false;
        formatter = null;
        var methodInfo = methodCallExpr.Method;
        var parameterInfos = methodInfo.GetParameters();
        var cacheKey = HashCode.Combine(methodInfo.DeclaringType, methodInfo);
        switch (methodInfo.Name)
        {
            case "ToBoolean":
            case "ToByte":
            case "ToChar":
            case "ToDateTime":
            case "ToDouble":
            case "ToInt16":
            case "ToInt32":
            case "ToInt64":
            case "ToSByte":
            case "ToSingle":
            case "ToUInt16":
            case "ToUInt32":
            case "ToUInt64":
            case "ToDecimal":
                if (parameterInfos.Length == 1)
                {
                    methodCallSqlFormatterCache.TryAdd(cacheKey, formatter = (visitor, target, deferExprs, args) =>
                    {
                        args[0] = visitor.VisitAndDeferred(args[0]);
                        if (args[0].IsConstant || args[0].IsVariable)
                            return args[0].Change(this.GetQuotedValue(methodCallExpr.Type, args[0]));

                        target.Merge(args[0]);
                        return target.Change(this.CastTo(methodCallExpr.Type, this.GetQuotedValue(args[0])), false, false, true);
                    });
                    result = true;
                }
                break;
            case "ToString":
                if (parameterInfos.Length == 1)
                {
                    methodCallSqlFormatterCache.TryAdd(cacheKey, formatter = (visitor, target, deferExprs, args) =>
                    {
                        args[0] = visitor.VisitAndDeferred(args[0]);
                        if (args[0].IsConstant)
                            return args[0].Change(this.GetQuotedValue(methodCallExpr.Type, args[0]));

                        target.Merge(args[0]);
                        return target.Change(this.CastTo(methodCallExpr.Type, this.GetQuotedValue(args[0])), false, false, true);
                    });
                    result = true;
                }
                break;
        }
        return result;
    }
}