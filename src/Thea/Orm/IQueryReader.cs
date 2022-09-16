using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Thea.Orm;

public interface IQueryReader
{
    TEntity Read<TEntity>();
    List<TEntity> ReadList<TEntity>();
    IPagedList<TEntity> ReadPageList<TEntity>();
    void Dispose();

    Task<TEntity> ReadAsync<TEntity>(CancellationToken cancellationToken = default);
    Task<List<TEntity>> ReadListAsync<TEntity>(CancellationToken cancellationToken = default);
    Task<IPagedList<TEntity>> ReadPageListAsync<TEntity>(CancellationToken cancellationToken = default);
    Task ReadNextResultAsync(CancellationToken cancellationToken = default);
    Task DisposeAsync();
}
