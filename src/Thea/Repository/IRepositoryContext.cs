using System;

namespace Thea
{
    public interface IRepositoryContext : IUnitOfWork, IDisposable
    {
        IRepository Create();
        void Close();
    }
}
