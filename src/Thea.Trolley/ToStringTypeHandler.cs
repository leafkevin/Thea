﻿using System;
using System.Data;
using Thea.Orm;

namespace Thea.Trolley;

public class ToStringTypeHandler : ITypeHandler
{
    public virtual void SetValue(IOrmProvider ormProvider, IDbDataParameter parameter, object value)
    {
        if (value == null)
            parameter.Value = DBNull.Value;
        else parameter.Value = value.ToString();
    }
    public virtual object Parse(IOrmProvider ormProvider, Type TargetType, object value)
    {
        if (value is DBNull) return null;
        return value;
    }
}
