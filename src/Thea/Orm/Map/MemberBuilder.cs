﻿using System;

namespace Thea.Orm;

public class MemberBuilder<TMember>
{
    private readonly MemberMap mapper;

    public MemberBuilder(MemberMap mapper) => this.mapper = mapper;

    public virtual MemberBuilder<TMember> Name(string memberName)
    {
        if (string.IsNullOrEmpty(memberName))
            throw new ArgumentNullException(nameof(memberName));

        this.mapper.MemberName = memberName;
        return this;
    }
    public virtual MemberBuilder<TMember> Field(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            throw new ArgumentNullException(nameof(fieldName));

        this.mapper.FieldName = fieldName;
        return this;
    }
    public virtual MemberBuilder<TMember> NativeDbType(object nativeDbType)
    {
        if (nativeDbType == null)
            throw new ArgumentNullException(nameof(nativeDbType));

        this.mapper.NativeDbType = nativeDbType;
        return this;
    }
    public virtual MemberBuilder<TMember> AutoIncrement()
    {
        this.mapper.IsAutoIncrement = true;
        return this;
    }
    public virtual MemberBuilder<TMember> TypeHandler(ITypeHandler typeHandler)
    {
        if (typeHandler == null)
            throw new ArgumentNullException(nameof(typeHandler));

        this.mapper.TypeHandler = typeHandler;
        return this;
    }
    public virtual MemberBuilder<TMember> TypeHandler<TTypeHandler>() where TTypeHandler : class, ITypeHandler, new()
    {
        this.mapper.TypeHandler = new TTypeHandler();
        return this;
    }
    public virtual MemberBuilder<TMember> Ignore()
    {
        this.mapper.IsIgnore = true;
        return this;
    }
}
