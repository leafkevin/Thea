namespace Thea.Orm;

public interface IUnitOfWork
{
    void BeginTransaction();
    void Commit();
    void Rollback();
}
