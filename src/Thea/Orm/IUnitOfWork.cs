namespace Thea.Orm;

public interface IUnitOfWork
{
    void Begin();
    void Commit();
    void Rollback();
}
