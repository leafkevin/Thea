﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using Thea.Orm;

namespace Thea.Trolley;

class IncludableQuery<T, TMember> : Query<T>, IIncludableQuery<T, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, TMember> : Query<T1, T2>, IIncludableQuery<T1, T2, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, TMember> : Query<T1, T2, T3>, IIncludableQuery<T1, T2, T3, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, TMember> : Query<T1, T2, T3, T4>, IIncludableQuery<T1, T2, T3, T4, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, TMember> : Query<T1, T2, T3, T4, T5>, IIncludableQuery<T1, T2, T3, T4, T5, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, TMember> : Query<T1, T2, T3, T4, T5, T6>, IIncludableQuery<T1, T2, T3, T4, T5, T6, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, TMember> : Query<T1, T2, T3, T4, T5, T6, T7>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8, T9>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor, int withIndex)
        : base(connection, transaction, visitor, withIndex) { }

    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TNavigation> ThenInclude<TNavigation>(Expression<Func<TMember, TNavigation>> member)
    {
        this.visitor.ThenInclude(member);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TNavigation>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
    public IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TElment> ThenIncludeMany<TElment>(Expression<Func<TMember, IEnumerable<TElment>>> member, Expression<Func<TElment, bool>> filter = null)
    {
        this.visitor.ThenInclude(member, true, filter);
        return new IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TElment>(this.connection, this.transaction, this.visitor, this.withIndex);
    }
}
class IncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TMember> : Query<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>, IIncludableQuery<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TMember>
{
    public IncludableQuery(TheaConnection connection, IDbTransaction transaction, IQueryVisitor visitor)
        : base(connection, transaction, visitor) { }
}