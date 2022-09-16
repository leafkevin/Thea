using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Thea.App;

namespace Thea
{
    public interface IQueryReader
    {
        TEntity Read<TEntity>();
        TTarget Read<TEntity, TTarget>();
        List<TEntity> ReadList<TEntity>();
        List<TTarget> ReadList<TEntity, TTarget>();
        IPagedList<TEntity> ReadPageList<TEntity>();
        IPagedList<TTarget> ReadPageList<TEntity, TTarget>();
        void Dispose();

        Task<TEntity> ReadAsync<TEntity>(CancellationToken cancellationToken = default);
        Task<TTarget> ReadAsync<TEntity, TTarget>(CancellationToken cancellationToken = default);
        Task<List<TEntity>> ReadListAsync<TEntity>(CancellationToken cancellationToken = default);
        Task<List<TTarget>> ReadListAsync<TEntity, TTarget>(CancellationToken cancellationToken = default);
        Task<IPagedList<TEntity>> ReadPageListAsync<TEntity>(CancellationToken cancellationToken = default);
        Task<IPagedList<TTarget>> ReadPageListAsync<TEntity, TTarget>(CancellationToken cancellationToken = default);
        Task ReadNextResultAsync(CancellationToken cancellationToken = default);
        Task DisposeAsync();
    }
}
